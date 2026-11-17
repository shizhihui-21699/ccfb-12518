namespace RedactionCheck.Core.Models;

/// <summary>Outcome of the mandatory re-check after real redaction.</summary>
public sealed class VerificationResult
{
    public required bool Passed { get; init; }
    public required int RemainingFindings { get; init; }
    /// <summary>Spot-checks: strings that were leaked in the original file and must no longer extract.</summary>
    public required List<VerificationProbe> Probes { get; init; } = new();
    public string? Error { get; init; }

    public static VerificationResult Fail(string error) =>
        new() { Passed = false, RemainingFindings = -1, Probes = new(), Error = error };
}

public sealed class VerificationProbe
{
    public required int PageNumber { get; init; }
    public required string OriginalText { get; init; }
    public required bool StillExtractable { get; init; }
}
