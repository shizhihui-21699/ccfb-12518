namespace RedactionCheck.Core.Models;

public sealed class PageReport
{
    public required int PageNumber { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public List<CoverElement> Covers { get; init; } = new();
    public List<LeakFinding> Findings { get; init; } = new();
    /// <summary>True redaction leaves no findings; this flag drives the re-check badge.</summary>
    public bool HasLeak => Findings.Count > 0;
}

public sealed class ScanReport
{
    public required string FileName { get; init; }
    public required int PageCount { get; init; }
    public List<PageReport> Pages { get; init; } = new();

    public int TotalFindings => Pages.Sum(p => p.Findings.Count);
    public int TotalLeakedWords => Pages.Sum(p => p.Findings.Sum(f => f.Words.Count));
    public bool HasAnyLeak => Pages.Any(p => p.HasLeak);
    public IEnumerable<LeakFinding> AllFindings => Pages.SelectMany(p => p.Findings);
}
