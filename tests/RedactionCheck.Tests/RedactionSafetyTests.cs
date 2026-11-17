using RedactionCheck.Core.Models;
using RedactionCheck.Core.Redaction;
using RedactionCheck.Core.Scanner;
using RedactionCheck.SampleGenerator;
using UglyToad.PdfPig;

namespace RedactionCheck.Tests;

public class RedactionSafetyTests
{
    private static byte[] Redact(byte[] pdf)
    {
        var report = new PdfLeakScanner().Scan(pdf, "x.pdf");
        return new PdfRedactor().Redact(pdf, report).PdfBytes;
    }

    [Fact]
    public void Redacted_pdf_is_a_valid_reopenable_document()
    {
        var redacted = Redact(SampleFileBuilder.MultiPageMixed());

        using var doc = PdfDocument.Open(redacted, new UglyToad.PdfPig.ParsingOptions { SkipMissingFonts = true });
        Assert.Equal(3, doc.NumberOfPages);
        // Every page must still yield extractable text objects (or an empty but
        // parseable page) rather than a corrupt content stream.
        foreach (var page in doc.GetPages())
            _ = page.Text;
    }

    [Fact]
    public void Redaction_is_idempotent_on_an_already_clean_file()
    {
        // Redacting once, then scanning the result, must stay clean and a second
        // redaction run must not throw.
        var once = Redact(SampleFileBuilder.FilledRectangle());
        var report = new PdfLeakScanner().Scan(once, "once.pdf");
        Assert.False(report.HasAnyLeak);

        var twice = new PdfRedactor().Redact(once, report);
        Assert.True(twice.Verification.Passed);
    }

    [Fact]
    public void Public_text_on_uncovered_lines_is_preserved()
    {
        var redacted = Redact(SampleFileBuilder.ThickStrokeBars());
        using var doc = PdfDocument.Open(redacted, new UglyToad.PdfPig.ParsingOptions { SkipMissingFonts = true });
        var text = doc.GetPages().First().Text;
        Assert.Contains("本页其余内容可以对外披露", text);
        Assert.DoesNotContain("8,400,000", text);
        Assert.DoesNotContain("120,000", text);
    }

    [Fact]
    public void Every_probe_for_original_secret_reports_not_extractable()
    {
        var pdf = SampleFileBuilder.OpaqueImageCover();
        var report = new PdfLeakScanner().Scan(pdf, "img.pdf");
        var result = new PdfRedactor().Redact(pdf, report);

        Assert.NotEmpty(result.Verification.Probes);
        Assert.All(result.Verification.Probes, p => Assert.False(p.StillExtractable));
    }
}
