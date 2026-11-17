using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.IO;
using RedactionCheck.Core.Fonts;
using RedactionCheck.Core.Geometry;
using RedactionCheck.Core.Models;
using RedactionCheck.Core.Scanner;
using UglyToad.PdfPig;
using PigPdfDocument = UglyToad.PdfPig.PdfDocument;

namespace RedactionCheck.Core.Redaction;

/// <summary>
/// Performs real redaction: opens the PDF with PdfSharp, removes the text and
/// image/path data inside every leaked region from the page content streams,
/// deletes covering annotations, paints an opaque black box over each region,
/// and saves a new file. The original bytes are never modified in place.
/// </summary>
public sealed class PdfRedactor
{
    private readonly PdfLeakScanner _scanner = new();

    public RedactionResult Redact(byte[] pdfBytes, ScanReport report)
    {
        var warnings = new List<string>();

        // 1. Re-scan the exact bytes we are about to edit, so page/glyph order
        //    matches the rewriter's PageGlyphIndex one-to-one.
        var freshReport = _scanner.Scan(pdfBytes, report.FileName);
        var zonesByPage = BuildZonesByPage(freshReport);

        // 2. PdfPig view: provides ordered glyph geometry per page.
        using var pigDoc = PigPdfDocument.Open(pdfBytes,
            new ParsingOptions { SkipMissingFonts = true });

        // 3. PdfSharp view: mutable document that will produce the output.
        using var inputMs = new MemoryStream(pdfBytes);
        using var sharpDoc = PdfReader.Open(inputMs, PdfDocumentOpenMode.Modify);

        var removedRuns = 0;
        var removedImages = 0;
        var removedAnnots = 0;
        var removedPaths = 0;

        for (var i = 0; i < sharpDoc.PageCount; i++)
        {
            var sharpPage = sharpDoc.Pages[i];
            var pigPage = pigDoc.GetPages().ElementAt(i);
            var pageNumber = i + 1;
            if (!zonesByPage.TryGetValue(pageNumber, out var zones) || zones.Count == 0)
                continue;

            var glyphIndex = new PageGlyphIndex(pigPage);

            var streamStats = RewritePageContents(sharpPage, zones, glyphIndex);
            removedRuns += streamStats.GlyphRuns;
            removedImages += streamStats.Images;
            removedPaths += streamStats.PathPaints;
            removedAnnots += RemoveCoveringAnnotations(sharpPage, zones);
            PaintRedactionBoxes(sharpPage, zones);
        }

        byte[] output;
        using (var outMs = new MemoryStream())
        {
            sharpDoc.Save(outMs, false);
            output = outMs.ToArray();
        }

        // 4. Mandatory re-check: the new file must contain none of the leaks.
        var verification = Verify(output, freshReport);

        return new RedactionResult
        {
            PdfBytes = output,
            OutputFileName = DeriveName(report.FileName),
            RedactedFindingCount = freshReport.TotalFindings,
            RemovedGlyphRuns = removedRuns,
            RemovedImages = removedImages,
            RemovedAnnotations = removedAnnots,
            Verification = verification,
            Warnings = warnings
        };
    }

    private static Dictionary<int, List<PdfRect>> BuildZonesByPage(ScanReport report)
    {
        var map = new Dictionary<int, List<PdfRect>>();
        foreach (var finding in report.AllFindings)
        {
            // Redact the whole cover box: everything underneath it must go,
            // including parts of glyphs the overlap heuristic did not flag.
            if (!map.TryGetValue(finding.PageNumber, out var list))
            {
                list = new List<PdfRect>();
                map[finding.PageNumber] = list;
            }
            if (!list.Any(z => z.IntersectsWith(finding.Cover.Bounds)
                               && z.IntersectionOverlapOf(finding.Cover.Bounds) > 0.9))
            {
                list.Add(finding.Cover.Bounds);
            }
        }
        return map;
    }

    private sealed record StreamStats(int GlyphRuns, int Images, int PathPaints);

    /// <summary>
    /// Rewrites every content stream of the page. Each stream gets its own
    /// <see cref="ContentStreamRewriter"/> (fresh graphics/text state) but they
    /// share one <see cref="PageGlyphIndex"/>, whose position-based lookup keeps
    /// glyph mapping consistent across stream boundaries.
    /// </summary>
    private static StreamStats RewritePageContents(PdfPage page, IReadOnlyList<PdfRect> zones,
        PageGlyphIndex glyphIndex)
    {
        var contents = page.Contents;
        var original = new List<PdfContent>();
        foreach (PdfContent content in contents) original.Add(content);

        var runs = 0;
        var images = 0;
        var paths = 0;
        var rewrittenStreams = new List<byte[]>(original.Count);

        foreach (var content in original)
        {
            var stream = content.Stream;
            stream.TryUnfilter();
            var decoded = stream.UnfilteredValue ?? stream.Value;

            var rewriter = new ContentStreamRewriter(zones, glyphIndex);
            rewrittenStreams.Add(rewriter.Rewrite(decoded));
            runs += rewriter.RemovedGlyphRuns;
            images += rewriter.RemovedImages;
            paths += rewriter.RemovedPathPaints;
        }

        for (var idx = 0; idx < original.Count; idx++)
            original[idx].Stream.Value = rewrittenStreams[idx];

        return new StreamStats(runs, images, paths);
    }

    private static int RemoveCoveringAnnotations(PdfPage page, IReadOnlyList<PdfRect> zones)
    {
        // HasAnnotations throws when the page dictionary has no /Annots entry at all.
        if (!page.Elements.ContainsKey("/Annots")) return 0;
        var removed = 0;
        var annots = page.Annotations;
        for (var i = annots.Count - 1; i >= 0; i--)
        {
            var annot = annots[i];
            if (annot.Rectangle == null) continue;
            var r = annot.Rectangle;
            var box = new PdfRect(Math.Min(r.X1, r.X2), Math.Min(r.Y1, r.Y2),
                                  Math.Max(r.X1, r.X2), Math.Max(r.Y1, r.Y2));
            if (zones.Any(z => z.IntersectsWith(box)))
            {
                annots.Remove(annot);
                removed++;
            }
        }
        return removed;
    }

    private static void PaintRedactionBoxes(PdfPage page, IReadOnlyList<PdfRect> zones)
    {
        // Append a final content stream painting opaque black rectangles so the
        // redacted regions stay visually covered even though the text is gone.
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("q");
        sb.AppendLine("0 0 0 rg");
        foreach (var z in zones)
        {
            // PDF rect operator: x y width height re  (bottom-left origin).
            sb.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"{z.Left:0.###} {z.Bottom:0.###} {z.Width:0.###} {z.Height:0.###} re ");
            sb.AppendLine("f");
        }
        sb.AppendLine("Q");

        var overlay = page.Contents.AppendContent();
        overlay.CreateStream(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static VerificationResult Verify(byte[] redactedBytes, ScanReport originalReport)
    {
        try
        {
            // Structural check: no cover-over-text leaks remain.
            var reReport = new PdfLeakScanner().Scan(redactedBytes, originalReport.FileName);

            // Content check: every word that was recoverable in the original must
            // no longer appear in the new file's extracted text, even outside a
            // detected cover. This is independent of the scanner's overlap rules.
            var fullTextByPage = ExtractTextByPage(redactedBytes);

            var probes = originalReport.AllFindings
                .SelectMany(f => f.Words
                    .Where(w => w.Text.Trim().Length >= 2)
                    .Select(w => new VerificationProbe
                    {
                        PageNumber = f.PageNumber,
                        OriginalText = w.Text,
                        StillExtractable = fullTextByPage.TryGetValue(f.PageNumber, out var txt)
                            && txt.Contains(w.Text, StringComparison.Ordinal)
                    }))
                .GroupBy(p => (p.PageNumber, p.OriginalText))
                .Select(g => g.First())
                .ToList();

            var passed = reReport.TotalFindings == 0 && probes.All(p => !p.StillExtractable);
            return new VerificationResult
            {
                Passed = passed,
                RemainingFindings = reReport.TotalFindings,
                Probes = probes
            };
        }
        catch (Exception ex)
        {
            return VerificationResult.Fail(ex.Message);
        }
    }

    private static Dictionary<int, string> ExtractTextByPage(byte[] pdfBytes)
    {
        using var doc = PigPdfDocument.Open(pdfBytes,
            new ParsingOptions { SkipMissingFonts = true });
        var map = new Dictionary<int, string>();
        foreach (var page in doc.GetPages())
            map[page.Number] = page.Text ?? string.Empty;
        return map;
    }

    private static string DeriveName(string original)
    {
        var stem = Path.GetFileNameWithoutExtension(original);
        return $"{stem}.redacted.pdf";
    }
}
