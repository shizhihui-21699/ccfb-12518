namespace RedactionCheck.Core.Models;

/// <summary>
/// One place where extractable text sits underneath a covering element.
/// A finding groups the words that share the same covering element.
/// </summary>
public sealed class LeakFinding
{
    public required int PageNumber { get; init; }
    public required int FindingIndex { get; init; }
    public required CoverElement Cover { get; init; }
    /// <summary>Words (in reading order) whose glyphs intersect the cover.</summary>
    public required List<LeakedWord> Words { get; init; } = new();

    /// <summary>The reconstructed text a recipient can copy out from under the box.</summary>
    public string RecoverableText => string.Join(' ', Words.Select(w => w.Text));

    /// <summary>Union box of the leaked glyphs, used to plan real redaction.</summary>
    public Geometry.PdfRect LeakedBounds =>
        Words.Aggregate(Words[0].Bounds, (acc, w) => acc.Union(w.Bounds));
}

public sealed class LeakedWord
{
    public required string Text { get; init; }
    public required Geometry.PdfRect Bounds { get; init; }
    /// <summary>0..1 share of the word's area inside the cover (diagnostic).</summary>
    public required double CoveredFraction { get; init; }
}
