using RedactionCheck.Core.Geometry;
using RedactionCheck.Core.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;

namespace RedactionCheck.Core.Scanner;

/// <summary>
/// Scans a PDF for "fake redaction": graphical elements that visually cover
/// text while the covered text remains in the content stream and is therefore
/// still copy/extract-able underneath.
/// </summary>
public sealed class PdfLeakScanner
{
    /// <summary>Minimum fraction of a word box that must sit under a cover to count as leaked.</summary>
    public const double MinWordCoveredFraction = 0.25;

    public ScanReport Scan(byte[] pdfBytes, string fileName)
    {
        using var doc = PdfDocument.Open(pdfBytes, new ParsingOptions { SkipMissingFonts = true });
        var report = new ScanReport { FileName = fileName, PageCount = doc.NumberOfPages };

        foreach (var page in doc.GetPages())
        {
            var pageReport = new PageReport
            {
                PageNumber = page.Number,
                Width = page.Width,
                Height = page.Height
            };

            var walker = new GraphicsStateWalker(page.Number);
            walker.Walk(page.Operations);
            pageReport.Covers.AddRange(walker.Covers);
            AddAnnotationCovers(page, pageReport.Covers);

            var words = page.GetWords().ToList();
            pageReport.Findings.AddRange(BuildFindings(page.Number, pageReport.Covers, words));

            report.Pages.Add(pageReport);
        }

        return report;
    }

    private static void AddAnnotationCovers(Page page, List<CoverElement> covers)
    {
        IReadOnlyList<Annotation> annotations;
        try
        {
            annotations = page.GetAnnotations().ToList();
        }
        catch (Exception)
        {
            // Annotation parsing is best-effort; a malformed annot must not abort the scan.
            return;
        }

        foreach (var a in annotations)
        {
            // Only markup-style annotations that actually paint over content are relevant.
            if (!PaintsOverContent(a.Type)) continue;
            var r = a.Rectangle;
            if (r.Width <= 1 || r.Height <= 1) continue;

            covers.Add(new CoverElement
            {
                PageNumber = page.Number,
                Kind = CoverKind.Annotation,
                Bounds = new PdfRect(r.Left, r.Bottom, r.Right, r.Top),
                Description = $"注释 {a.Type}（{a.Name ?? "未命名"}）",
                AnnotationText = a.Content
            });
        }
    }

    private static bool PaintsOverContent(AnnotationType type) => type switch
    {
        AnnotationType.Square or AnnotationType.Circle or AnnotationType.FreeText
            or AnnotationType.Highlight or AnnotationType.StrikeOut or AnnotationType.Squiggly
            or AnnotationType.Underline or AnnotationType.Stamp or AnnotationType.Ink
            or AnnotationType.Polygon or AnnotationType.Line or AnnotationType.Watermark => true,
        _ => false,
    };

    private static IEnumerable<LeakFinding> BuildFindings(
        int pageNumber, IReadOnlyList<CoverElement> covers, IReadOnlyList<Word> words)
    {
        var index = 0;
        foreach (var cover in covers)
        {
            var leaked = new List<LeakedWord>();
            foreach (var word in words)
            {
                var box = ToRect(word.BoundingBox);
                if (!box.IntersectsWith(cover.Bounds)) continue;
                var fraction = cover.Bounds.IntersectionOverlapOf(box);
                if (fraction < MinWordCoveredFraction) continue;
                leaked.Add(new LeakedWord
                {
                    Text = word.Text,
                    Bounds = box,
                    CoveredFraction = fraction
                });
            }

            if (leaked.Count > 0)
            {
                yield return new LeakFinding
                {
                    PageNumber = pageNumber,
                    FindingIndex = index++,
                    Cover = cover,
                    Words = SortReadingOrder(leaked)
                };
            }
        }
    }

    private static List<LeakedWord> SortReadingOrder(List<LeakedWord> words)
        => words.OrderBy(w => Math.Round(w.Bounds.Top, 0))
                .ThenBy(w => w.Bounds.Left)
                .ToList();

    private static PdfRect ToRect(UglyToad.PdfPig.Core.PdfRectangle r)
        => new(r.Left, r.Bottom, r.Right, r.Top);
}
