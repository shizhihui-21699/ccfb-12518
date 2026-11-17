using RedactionCheck.Core.Models;
using RedactionCheck.Core.Redaction;
using RedactionCheck.Core.Scanner;

namespace RedactionCheck.Web.Services;

/// <summary>
/// Holds the most recently scanned/redacted document for one browser circuit.
/// Scoped (not singleton) so different legal users never see each other's files.
/// </summary>
public sealed class AnalysisSession
{
    public byte[]? OriginalBytes { get; private set; }
    public string? OriginalName { get; private set; }
    public ScanReport? Report { get; private set; }
    public RedactionResult? LastRedaction { get; private set; }
    public string? Error { get; private set; }
    public bool Busy { get; private set; }

    public void MarkBusy() => Busy = true;
    public void MarkIdle() => Busy = false;

    public void StoreOriginal(byte[] bytes, string name)
    {
        OriginalBytes = bytes;
        OriginalName = name;
        LastRedaction = null;
        Error = null;
    }

    public void StoreReport(ScanReport report)
    {
        Report = report;
        Busy = false;
    }

    public void StoreRedaction(RedactionResult result)
    {
        LastRedaction = result;
        Busy = false;
    }

    public void Fail(string error)
    {
        Error = error;
        Busy = false;
    }

    /// <summary>Returns the session to its initial state for a brand-new upload.</summary>
    public void Clear()
    {
        OriginalBytes = null;
        OriginalName = null;
        Report = null;
        LastRedaction = null;
        Error = null;
        Busy = false;
    }
}

/// <summary>Application facade wrapping the scanner and redactor.</summary>
public sealed class PdfAnalysisService
{
    private readonly PdfLeakScanner _scanner = new();
    private readonly PdfRedactor _redactor = new();

    public ScanReport Scan(byte[] bytes, string fileName) => _scanner.Scan(bytes, fileName);

    public RedactionResult Redact(byte[] bytes, ScanReport report) => _redactor.Redact(bytes, report);
}
