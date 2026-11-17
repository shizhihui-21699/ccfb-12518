namespace RedactionCheck.Core.Models;

public sealed class RedactionResult
{
    public required byte[] PdfBytes { get; init; }
    public required string OutputFileName { get; init; }
    public required int RedactedFindingCount { get; init; }
    public required int RemovedGlyphRuns { get; init; }
    public required int RemovedImages { get; init; }
    public required int RemovedAnnotations { get; init; }
    public required VerificationResult Verification { get; init; }
    public List<string> Warnings { get; init; } = new();
}
