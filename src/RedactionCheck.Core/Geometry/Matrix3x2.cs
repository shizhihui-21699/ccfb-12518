namespace RedactionCheck.Core.Geometry;

/// <summary>
/// Affine transform [a b c d e f] as used by PDF's cm/Tm operators.
/// Point transform: x' = a*x + c*y + e ; y' = b*x + d*y + f.
/// </summary>
public readonly struct Matrix3x2 : IEquatable<Matrix3x2>
{
    public double A { get; }
    public double B { get; }
    public double C { get; }
    public double D { get; }
    public double E { get; }
    public double F { get; }

    public Matrix3x2(double a, double b, double c, double d, double e, double f)
    {
        A = a; B = b; C = c; D = d; E = e; F = f;
    }

    public static Matrix3x2 Identity => new(1, 0, 0, 1, 0, 0);

    public bool IsIdentity => A == 1 && B == 0 && C == 0 && D == 1 && E == 0 && F == 0;

    /// <summary>PDF multiplication: this * other (other is applied first to points).</summary>
    public Matrix3x2 Multiply(in Matrix3x2 o) => new(
        A * o.A + B * o.C,
        A * o.B + B * o.D,
        C * o.A + D * o.C,
        C * o.B + D * o.D,
        E * o.A + F * o.C + o.E,
        E * o.B + F * o.D + o.F);

    public (double X, double Y) Apply(double x, double y)
        => (A * x + C * y + E, B * x + D * y + F);

    public PdfRect ApplyToRect(PdfRect rect) => rect.Transform(this);

    public static Matrix3x2 Translation(double tx, double ty) => new(1, 0, 0, 1, tx, ty);
    public static Matrix3x2 Scaling(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    public bool Equals(Matrix3x2 other)
        => A.Equals(other.A) && B.Equals(other.B) && C.Equals(other.C)
        && D.Equals(other.D) && E.Equals(other.E) && F.Equals(other.F);

    public override bool Equals(object? obj) => obj is Matrix3x2 m && Equals(m);
    public override int GetHashCode() => HashCode.Combine(A, B, C, D, E, F);
    public override string ToString() => $"[{A:F3} {B:F3} {C:F3} {D:F3} {E:F3} {F:F3}]";
}
