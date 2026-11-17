using RedactionCheck.Core.Geometry;

namespace RedactionCheck.Core.Redaction;

/// <summary>
/// Mutable PDF text-object state (ISO 32000-1 clause 9.3) maintained while
/// rewriting a content stream. Glyph positions are mapped to page user space
/// through <c>CTM × Tm</c>.
/// </summary>
internal sealed class TextStreamState
{
    public Matrix3x2 Ctm = Matrix3x2.Identity;
    public Matrix3x2 Tlm = Matrix3x2.Identity;
    public Matrix3x2 Tm = Matrix3x2.Identity;
    public double Leading;
    public double CharSpacing;
    public double WordSpacing;
    public double HorizontalScaling = 100.0;
    public double Rise;
    public double FontSize = 1;
    public double FontScaleX => HorizontalScaling / 100.0;

    public bool InText { get; private set; }

    /// <summary>Full transform mapping text-space glyph positions to page user space.</summary>
    public Matrix3x2 TextToUser => Ctm.Multiply(Tm);

    public void BeginTextObject()
    {
        InText = true;
        Tlm = Matrix3x2.Identity;
        Tm = Matrix3x2.Identity;
    }

    public void EndTextObject() => InText = false;

    public void SetTextMatrix(Matrix3x2 m)
    {
        Tm = m;
        Tlm = m;
    }

    public void NewLine(double tx, double ty)
    {
        // Td/TD: Tm = Tlm × [1 0 0 1 tx ty]
        Tm = Tlm.Multiply(Matrix3x2.Translation(tx, ty));
        Tlm = Tm;
    }

    public void NewLineWithLeading(double tx, double ty)
    {
        Leading = -ty;
        NewLine(tx, ty);
    }
}
