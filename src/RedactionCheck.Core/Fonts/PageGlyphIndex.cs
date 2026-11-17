using RedactionCheck.Core.Geometry;
using UglyToad.PdfPig.Content;

namespace RedactionCheck.Core.Fonts;

/// <summary>
/// Index of every glyph PdfPig located on a page, in reading order.
/// The rewriter looks each glyph up by its current user-space text origin
/// instead of blindly consuming in sequence, so matching stays correct even
/// when a page is split across several content streams (each stream restarts
/// its BT/Td operators, but glyph positions on the page remain unique).
/// </summary>
internal sealed class PageGlyphIndex
{
    private readonly List<GlyphInfo> _glyphs;
    private int _searchFrom;

    public PageGlyphIndex(Page page)
    {
        _glyphs = page.Letters
            .Where(l => !string.IsNullOrEmpty(l.Value))
            .Select(l => new GlyphInfo(
                l.Value,
                new PdfRect(l.BoundingBox.Left, l.BoundingBox.Bottom,
                            l.BoundingBox.Right, l.BoundingBox.Top),
                l.Width,
                l.StartBaseLine.X,
                l.StartBaseLine.Y))
            .ToList();
    }

    public int Count => _glyphs.Count;

    /// <summary>
    /// Returns the glyph whose baseline origin is nearest <paramref name="userX"/>,
    /// <paramref name="userY"/> among the not-yet-matched glyphs, or null when no
    /// candidate lies within <paramref name="tolerancePoints"/>.
    /// </summary>
    public GlyphInfo? MatchAt(double userX, double userY, double tolerancePoints = 3.0)
    {
        var best = -1;
        var bestDist = tolerancePoints;
        for (var i = _searchFrom; i < _glyphs.Count; i++)
        {
            var g = _glyphs[i];
            var dx = g.BaseX - userX;
            var dy = g.BaseY - userY;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist <= bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        if (best < 0) return null;
        // Everything before the match is considered consumed; the cursor only
        // ever moves forward, matching reading order across all content streams.
        _searchFrom = best + 1;
        return _glyphs[best];
    }

    /// <summary>
    /// Consumes the next glyph in reading order without a position check.
    /// Used for subsequent characters of the same show string, which advance
    /// linearly from the matched first glyph.
    /// </summary>
    public GlyphInfo? ConsumeNext()
        => _searchFrom < _glyphs.Count ? _glyphs[_searchFrom++] : null;

    public void Reset() => _searchFrom = 0;
}

internal sealed record GlyphInfo(string Value, PdfRect Bounds, double Width, double BaseX, double BaseY);
