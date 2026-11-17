namespace RedactionCheck.Core.Models;

/// <summary>How a covering element was drawn.</summary>
public enum CoverKind
{
    /// <summary>f/f* path fill that paints a solid area (the classic "black box").</summary>
    FilledArea,
    /// <summary>A stroked path whose line width is large enough to act as a cover bar.</summary>
    ThickStroke,
    /// <summary>An opaque raster image XObject painted over text.</summary>
    OpaqueImage,
    /// <summary>A square/circle/stamp/highlight annotation rendered on top of text.</summary>
    Annotation,
}

/// <summary>
/// One graphical element that visually covers part of a page while the
/// underlying content stream still contains the original text.
/// </summary>
public sealed class CoverElement
{
    public required int PageNumber { get; init; }
    public required CoverKind Kind { get; init; }
    /// <summary>Bounding box in PDF user space (bottom-left origin, points).</summary>
    public required Geometry.PdfRect Bounds { get; init; }
    /// <summary>Human-readable description, e.g. "填充路径 (0,0,0)".</summary>
    public required string Description { get; init; }
    /// <summary>For annotations: the annotation's /Contents string, if any.</summary>
    public string? AnnotationText { get; init; }
}
