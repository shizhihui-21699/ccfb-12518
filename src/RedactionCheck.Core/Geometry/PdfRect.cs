namespace RedactionCheck.Core.Geometry;

/// <summary>
/// Axis-aligned rectangle in PDF user space. PDF origin is the bottom-left
/// corner, y grows upwards. Values are in PDF points (1/72 inch).
/// </summary>
public readonly struct PdfRect : IEquatable<PdfRect>
{
    public double Left { get; }
    public double Bottom { get; }
    public double Right { get; }
    public double Top { get; }

    public PdfRect(double left, double bottom, double right, double top)
    {
        // Normalise so callers can never construct an inverted box.
        Left = Math.Min(left, right);
        Right = Math.Max(left, right);
        Bottom = Math.Min(bottom, top);
        Top = Math.Max(bottom, top);
    }

    public double Width => Right - Left;
    public double Height => Top - Bottom;
    public double Area => Math.Max(0, Width) * Math.Max(0, Height);

    public static PdfRect FromLtrb(double left, double top, double right, double bottom)
        => new(left, bottom, right, top);

    public static PdfRect FromPoints(IEnumerable<(double X, double Y)> points)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        bool any = false;
        foreach (var (x, y) in points)
        {
            any = true;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }
        return any ? new PdfRect(minX, minY, maxX, maxY) : default;
    }

    public bool IntersectsWith(PdfRect other)
        => Left < other.Right && Right > other.Left && Bottom < other.Top && Top > other.Bottom;

    /// <summary>Fraction of <paramref name="other"/>'s area that lies inside this box.</summary>
    public double IntersectionOverlapOf(PdfRect other)
    {
        if (other.Area <= 0) return 0;
        var w = Math.Min(Right, other.Right) - Math.Max(Left, other.Left);
        var h = Math.Min(Top, other.Top) - Math.Max(Bottom, other.Bottom);
        if (w <= 0 || h <= 0) return 0;
        return (w * h) / other.Area;
    }

    public PdfRect Union(PdfRect other)
        => new(Math.Min(Left, other.Left), Math.Min(Bottom, other.Bottom),
               Math.Max(Right, other.Right), Math.Max(Top, other.Top));

    public PdfRect Inflate(double amount)
        => new(Left - amount, Bottom - amount, Right + amount, Top + amount);

    /// <summary>Bounding box of the four corners transformed through <paramref name="m"/>.</summary>
    public PdfRect Transform(Matrix3x2 m)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (cx, cy) in Corners())
        {
            var (x, y) = m.Apply(cx, cy);
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        return new PdfRect(minX, minY, maxX, maxY);
    }

    private IEnumerable<(double X, double Y)> Corners()
    {
        yield return (Left, Bottom);
        yield return (Right, Bottom);
        yield return (Right, Top);
        yield return (Left, Top);
    }

    public bool Equals(PdfRect other)
        => Left.Equals(other.Left) && Bottom.Equals(other.Bottom)
        && Right.Equals(other.Right) && Top.Equals(other.Top);

    public override bool Equals(object? obj) => obj is PdfRect r && Equals(r);
    public override int GetHashCode() => HashCode.Combine(Left, Bottom, Right, Top);
    public override string ToString() => $"L{Left:F1} B{Bottom:F1} R{Right:F1} T{Top:F1} ({Width:F1}x{Height:F1})";
}
