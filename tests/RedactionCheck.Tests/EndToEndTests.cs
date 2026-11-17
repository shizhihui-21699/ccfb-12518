using RedactionCheck.Core.Models;
using RedactionCheck.Core.Redaction;
using RedactionCheck.Core.Scanner;
using RedactionCheck.SampleGenerator;

namespace RedactionCheck.Tests;

public class EndToEndTests
{
    public static IEnumerable<object[]> AllSamples()
    {
        yield return new object[] { "01 填充黑框", SampleFileBuilder.FilledRectangle(), "6222" };
        yield return new object[] { "02 粗描边线", SampleFileBuilder.ThickStrokeBars(), "8,400,000" };
        yield return new object[] { "03 注释方块", SampleFileBuilder.MarkupAnnotation(), "张伟" };
        yield return new object[] { "04 图片遮盖", SampleFileBuilder.OpaqueImageCover(), "3.2" };
        yield return new object[] { "05 变换裁剪", SampleFileBuilder.TransformedAndClipped(), "250,000" };
        yield return new object[] { "06 多页混合", SampleFileBuilder.MultiPageMixed(), "P@ssw0rd" };
    }

    [Theory]
    [MemberData(nameof(AllSamples))]
    public void Scanner_finds_recoverable_text_under_every_cover(string label, byte[] pdf, string secret)
    {
        var report = new PdfLeakScanner().Scan(pdf, label);

        Assert.True(report.HasAnyLeak, $"{label}: 应检出假涂黑");
        var allText = string.Join(' ', report.AllFindings.Select(f => f.RecoverableText));
        Assert.Contains(secret, allText);
    }

    [Theory]
    [MemberData(nameof(AllSamples))]
    public void Redaction_removes_text_and_recheck_passes(string label, byte[] pdf, string secret)
    {
        var scanner = new PdfLeakScanner();
        var report = scanner.Scan(pdf, label);

        var result = new PdfRedactor().Redact(pdf, report);

        Assert.True(result.Verification.Passed,
            $"{label}: 复查未通过，残留 {result.Verification.RemainingFindings} 处，错误 {result.Verification.Error}");
        Assert.All(result.Verification.Probes, p => Assert.False(p.StillExtractable));

        // Independently extract the full text of the redacted file and make sure
        // the secret is gone while unrelated public text survives.
        var postText = FullTextExtractor.Extract(result.PdfBytes);
        Assert.DoesNotContain(secret, postText);
    }

    [Fact]
    public void Clean_page_is_not_flagged_and_survives_redaction()
    {
        var pdf = SampleFileBuilder.MultiPageMixed();
        var report = new PdfLeakScanner().Scan(pdf, "multi");

        var cleanPage = report.Pages.Single(p => p.PageNumber == 2);
        Assert.False(cleanPage.HasLeak);
        Assert.True(report.Pages[0].HasLeak);
        Assert.True(report.Pages[2].HasLeak);

        var result = new PdfRedactor().Redact(pdf, report);
        Assert.True(result.Verification.Passed);
        var postText = FullTextExtractor.Extract(result.PdfBytes);
        Assert.Contains("公开信息", postText);
        Assert.DoesNotContain("P@ssw0rd", postText);
        Assert.DoesNotContain("42.80", postText);
    }
}

/// <summary>Independent extraction via PdfPig, mirroring what a recipient's copy-paste would get.</summary>
internal static class FullTextExtractor
{
    public static string Extract(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf,
            new UglyToad.PdfPig.ParsingOptions { SkipMissingFonts = true });
        return string.Join('\n', doc.GetPages().Select(p => p.Text));
    }
}
