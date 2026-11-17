using RedactionCheck.Core.Geometry;
using RedactionCheck.Core.Redaction;

namespace RedactionCheck.Tests;

public class GeometryAndTokenizerTests
{
    [Fact]
    public void Rect_normalises_inverted_coordinates()
    {
        var r = new PdfRect(10, 20, 5, 2);
        Assert.Equal(5, r.Left);
        Assert.Equal(2, r.Bottom);
        Assert.Equal(10, r.Right);
        Assert.Equal(20, r.Top);
        Assert.Equal(90, r.Area, 6); // width 5 * height 18
    }

    [Fact]
    public void Overlap_fraction_matches_intersection_area()
    {
        var cover = new PdfRect(0, 0, 10, 10);
        var half = new PdfRect(5, 0, 15, 10); // half inside the cover
        Assert.Equal(0.5, cover.IntersectionOverlapOf(half), 6);
        Assert.True(cover.IntersectsWith(half));

        var outside = new PdfRect(20, 20, 30, 30);
        Assert.False(cover.IntersectsWith(outside));
        Assert.Equal(0, cover.IntersectionOverlapOf(outside));
    }

    [Fact]
    public void Matrix_maps_translated_unit_square()
    {
        var m = new Matrix3x2(2, 0, 0, 3, 10, 20);
        var box = new PdfRect(0, 0, 1, 1).Transform(m);
        Assert.Equal(10, box.Left, 6);
        Assert.Equal(20, box.Bottom, 6);
        Assert.Equal(12, box.Right, 6);
        Assert.Equal(23, box.Top, 6);
    }

    [Fact]
    public void Tokenizer_handles_strings_hex_names_and_escapes()
    {
        var data = System.Text.Encoding.ASCII.GetBytes("(a\\)b) <4142> /Name#20X 1 2.5 Tj % comment\n");
        var tokens = ContentStreamTokenizer.Tokenize(data);

        Assert.Equal(ContentTokenKind.LiteralString, tokens[0].Kind);
        // Raw bytes keep the escape so the rewriter can pass untouched strings
        // through byte-for-byte; only rendering applies PDF escape semantics.
        Assert.Equal(@"a\)b", System.Text.Encoding.ASCII.GetString(tokens[0].Raw));

        var hex = tokens.First(t => t.Kind == ContentTokenKind.HexString);
        Assert.Equal("AB", System.Text.Encoding.ASCII.GetString(hex.Raw));

        var name = tokens.First(t => t.Kind == ContentTokenKind.Name);
        Assert.Equal("Name X", System.Text.Encoding.ASCII.GetString(name.Raw));

        Assert.Equal(1.0, tokens.First(t => t.Kind == ContentTokenKind.Integer).AsDouble());
        Assert.Equal(2.5, tokens.First(t => t.Kind == ContentTokenKind.Real).AsDouble());
        Assert.Equal("Tj", tokens.Last().Keyword);
    }
}
