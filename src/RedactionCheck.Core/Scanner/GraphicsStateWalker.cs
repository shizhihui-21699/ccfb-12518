using RedactionCheck.Core.Geometry;
using RedactionCheck.Core.Models;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Graphics.Operations.PathConstruction;
using UglyToad.PdfPig.Graphics.Operations.PathPainting;
using UglyToad.PdfPig.Graphics.Operations.SpecialGraphicsState;

namespace RedactionCheck.Core.Scanner;

/// <summary>
/// Walks one page's content-stream operations, maintaining the PDF graphics
/// state (CTM stack, fill/stroke colour, line width) and emits
/// <see cref="CoverElement"/> candidates whenever a path is filled, a thick
/// line is stroked, or an XObject image is painted.
/// </summary>
/// <remarks>
/// Coordinates are kept in page user space. Paths are reduced to their
/// bounding boxes, which is sufficient for redaction overlap tests.
/// </remarks>
internal sealed class GraphicsStateWalker
{
    private readonly int _pageNumber;
    private readonly List<CoverElement> _covers = new();

    // Graphics state stack (q ... Q).
    private readonly Stack<GState> _stack = new();
    private GState _current = GState.Default;

    // Path under construction, in user space.
    private readonly List<(double X, double Y)> _pathPoints = new();
    private bool _pathHasAnyPoint;
    private double _pathMinX, _pathMinY, _pathMaxX, _pathMaxY;

    public GraphicsStateWalker(int pageNumber) => _pageNumber = pageNumber;

    public IReadOnlyList<CoverElement> Covers => _covers;

    public void Walk(IReadOnlyList<IGraphicsStateOperation> operations)
    {
        foreach (var op in operations)
            Apply(op);
    }

    private void Apply(IGraphicsStateOperation op)
    {
        switch (op)
        {
            case Push:
                _stack.Push(_current);
                break;
            case Pop when _stack.Count > 0:
                _current = _stack.Pop();
                break;
            case ModifyCurrentTransformationMatrix cm:
                // cm concatenates: new = current * cm
                var m = new Matrix3x2(cm.Value[0], cm.Value[1], cm.Value[2],
                                      cm.Value[3], cm.Value[4], cm.Value[5]);
                _current = _current with { Ctm = _current.Ctm.Multiply(m) };
                break;

            // ---- colour state (non-stroke = fill, stroke = line colour) ----
            case UglyToad.PdfPig.Graphics.Operations.SetNonStrokeColorDeviceRgb rgb:
                _current = _current with { FillColor = $"({rgb.R:0.##},{rgb.G:0.##},{rgb.B:0.##})" };
                break;
            case UglyToad.PdfPig.Graphics.Operations.SetNonStrokeColorDeviceGray g:
                _current = _current with { FillColor = $"gray {g.Gray:0.##}" };
                break;
            case UglyToad.PdfPig.Graphics.Operations.SetNonStrokeColorDeviceCmyk c:
                _current = _current with { FillColor = $"cmyk({c.C:0.##},{c.M:0.##},{c.Y:0.##},{c.K:0.##})" };
                break;
            case UglyToad.PdfPig.Graphics.Operations.SetStrokeColorDeviceRgb rgb:
                _current = _current with { StrokeColor = $"({rgb.R:0.##},{rgb.G:0.##},{rgb.B:0.##})" };
                break;
            case UglyToad.PdfPig.Graphics.Operations.SetStrokeColorDeviceGray g:
                _current = _current with { StrokeColor = $"gray {g.Gray:0.##}" };
                break;
            case UglyToad.PdfPig.Graphics.Operations.General.SetLineWidth lw:
                _current = _current with { LineWidth = lw.Width };
                break;

            // ---- path construction ----
            case AppendRectangle rect:
                AddPathPoint(rect.LowerLeftX, rect.LowerLeftY);
                AddPathPoint(rect.LowerLeftX + rect.Width, rect.LowerLeftY + rect.Height);
                break;
            case BeginNewSubpath mv:
                AddPathPoint(mv.X, mv.Y);
                break;
            case AppendStraightLineSegment ln:
                AddPathPoint(ln.X, ln.Y);
                break;
            case AppendEndControlPointBezierCurve c:
                AddPathPoint(c.X1, c.Y1);
                AddPathPoint(c.X3, c.Y3);
                break;
            case AppendStartControlPointBezierCurve c:
                AddPathPoint(c.X2, c.Y2);
                AddPathPoint(c.X3, c.Y3);
                break;
            case AppendDualControlPointBezierCurve c:
                AddPathPoint(c.X1, c.Y1);
                AddPathPoint(c.X2, c.Y2);
                AddPathPoint(c.X3, c.Y3);
                break;
            case CloseSubpath:
                break;

            // ---- path painting ----
            case FillPathNonZeroWinding:
            case FillPathEvenOddRule:
            case FillPathNonZeroWindingCompatibility:
                EmitFilledArea(op.GetType().Name);
                ResetPath();
                break;
            case FillPathNonZeroWindingAndStroke:
            case FillPathEvenOddRuleAndStroke:
                EmitFilledArea(op.GetType().Name);
                EmitStroke();
                ResetPath();
                break;
            case CloseFillPathNonZeroWindingAndStroke:
            case CloseFillPathEvenOddRuleAndStroke:
                EmitFilledArea(op.GetType().Name);
                EmitStroke();
                ResetPath();
                break;
            case StrokePath:
            case CloseAndStrokePath:
                EmitStroke();
                ResetPath();
                break;
            case EndPath:
                ResetPath();
                break;

            // ---- images ----
            case InvokeNamedXObject xobj:
                _covers.Add(new CoverElement
                {
                    PageNumber = _pageNumber,
                    Kind = CoverKind.OpaqueImage,
                    Bounds = UnitSquare().Transform(_current.Ctm),
                    Description = $"XObject /{xobj.Name.Data}"
                });
                break;
        }
    }

    private static PdfRect UnitSquare() => new(0, 0, 1, 1);

    private void AddPathPoint(double x, double y)
    {
        var (tx, ty) = _current.Ctm.Apply(x, y);
        if (!_pathHasAnyPoint)
        {
            _pathMinX = _pathMaxX = tx;
            _pathMinY = _pathMaxY = ty;
            _pathHasAnyPoint = true;
        }
        else
        {
            _pathMinX = Math.Min(_pathMinX, tx); _pathMaxX = Math.Max(_pathMaxX, tx);
            _pathMinY = Math.Min(_pathMinY, ty); _pathMaxY = Math.Max(_pathMaxY, ty);
        }
        _pathPoints.Add((tx, ty));
    }

    private void ResetPath()
    {
        _pathPoints.Clear();
        _pathHasAnyPoint = false;
    }

    private PdfRect? PathBounds()
        => _pathHasAnyPoint ? new PdfRect(_pathMinX, _pathMinY, _pathMaxX, _pathMaxY) : null;

    private void EmitFilledArea(string operatorName)
    {
        var bounds = PathBounds();
        if (bounds is not { Area: > 1 }) return; // ignore hairline artifacts
        _covers.Add(new CoverElement
        {
            PageNumber = _pageNumber,
            Kind = CoverKind.FilledArea,
            Bounds = bounds.Value,
            Description = $"填充区域 {operatorName}，填充色 {_current.FillColor}"
        });
    }

    private void EmitStroke()
    {
        var bounds = PathBounds();
        if (bounds == null) return;
        // A stroke only behaves as a cover when its pen is wide enough to
        // hide glyphs: expand the path box by half the transformed line width.
        var lw = Math.Abs(_current.LineWidth);
        if (lw < 4) return; // thinner than ~11pt text cap height; not a cover bar
        var scale = AverageScale(_current.Ctm);
        var half = lw * scale / 2.0;
        _covers.Add(new CoverElement
        {
            PageNumber = _pageNumber,
            Kind = CoverKind.ThickStroke,
            Bounds = bounds.Value.Inflate(half),
            Description = $"粗描边线宽 {_current.LineWidth:0.#}pt，描边色 {_current.StrokeColor}"
        });
    }

    private static double AverageScale(in Matrix3x2 m)
    {
        // Mean of the basis-vector lengths of the affine transform.
        var sx = Math.Sqrt(m.A * m.A + m.B * m.B);
        var sy = Math.Sqrt(m.C * m.C + m.D * m.D);
        return (sx + sy) / 2.0;
    }

    private record GState(Matrix3x2 Ctm, string FillColor, string StrokeColor, double LineWidth)
    {
        public static GState Default { get; } =
            new(Matrix3x2.Identity, "(0,0,0)", "(0,0,0)", 1.0);
    }
}
