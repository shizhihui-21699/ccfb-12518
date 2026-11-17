using System.Globalization;
using System.Text;
using RedactionCheck.Core.Fonts;
using RedactionCheck.Core.Geometry;
using RedactionCheck.Core.Models;

namespace RedactionCheck.Core.Redaction;

/// <summary>
/// Rewrites one page's decoded content stream, removing text glyphs, image
/// paints and vector paths that intersect the redaction rectangles. Output is
/// a semantically equivalent stream in which every removed piece is replaced
/// either by a gap (TJ kerning) or an equivalent blank string.
/// </summary>
internal sealed class ContentStreamRewriter
{
    private readonly IReadOnlyList<PdfRect> _zones;
    private readonly PageGlyphIndex _glyphs;
    private readonly TextStreamState _text = new();
    private readonly List<string> _out = new();
    private readonly List<ContentToken> _pending = new();

    // Graphics state for vector/image removal.
    private readonly Stack<Matrix3x2> _ctmStack = new();
    private Matrix3x2 _ctm = Matrix3x2.Identity;
    private double _lineWidth = 1.0;

    // Path construction tracking.
    private readonly List<(double X, double Y)> _path = new();
    private bool _pathStarted;
    private double _pMinX, _pMinY, _pMaxX, _pMaxY;

    public int RemovedGlyphRuns { get; private set; }
    public int RemovedImages { get; private set; }
    public int RemovedPathPaints { get; private set; }

    public ContentStreamRewriter(IReadOnlyList<PdfRect> zones, PageGlyphIndex glyphs)
    {
        _zones = zones;
        _glyphs = glyphs;
    }

    public byte[] Rewrite(byte[] decodedContent)
    {
        var tokens = ContentStreamTokenizer.Tokenize(decodedContent);

        for (var i = 0; i < tokens.Count; i++)
        {
            // Non-keyword tokens are always operands. A keyword token is only an
            // operator when it is a real PDF operator; structural words such as
            // "true"/"false"/"null" or dictionary names stay with the operands.
            if (tokens[i].Kind != ContentTokenKind.Keyword || !Operators.Contains(tokens[i].Keyword))
            {
                _pending.Add(tokens[i]);
                continue;
            }

            var op = tokens[i].Keyword;
            var operands = DrainOperands();
            HandleOperator(op, operands);
        }
        return Encoding.UTF8.GetBytes(string.Join(" ", _out));
    }

    /// <summary>Every PDF content-stream operator the rewriter can encounter.</summary>
    private static readonly HashSet<string> Operators = new(new[]
    {
        // graphics state
        "q", "Q", "cm", "w", "J", "j", "M", "d", "ri", "i", "gs",
        // colour
        "CS", "cs", "SC", "SCN", "sc", "scn", "G", "g", "RG", "rg", "K", "k",
        // path construction
        "m", "l", "c", "v", "y", "h", "re",
        // path painting
        "S", "s", "f", "F", "f*", "B", "B*", "b", "b*", "n",
        // clipping
        "W", "W*",
        // images / xobjects / shading
        "BI", "ID", "EI", "Do", "sh",
        // text objects
        "BT", "ET",
        // text state
        "Tc", "Tw", "Tz", "TL", "Tf", "Tr", "Ts",
        // text positioning
        "Td", "TD", "Tm", "T*",
        // text showing
        "Tj", "TJ", "'", "\"",
        // marked content / compatibility
        "BMC", "BDC", "EMC", "MP", "DP", "BX", "EX",
    });

    private List<ContentToken> DrainOperands()
    {
        // Must build a fresh list: clearing the field in-place would empty the
        // alias we are about to return as well.
        var drained = new List<ContentToken>(_pending);
        _pending.Clear();
        return drained;
    }

    private void Emit(string s) => _out.Add(s);

    private void EmitOperands(List<ContentToken> operands)
    {
        foreach (var t in operands) Emit(RenderToken(t));
    }

    private void EmitPending()
    {
        EmitOperands(_pending);
        _pending.Clear();
    }

    private void HandleOperator(string op, List<ContentToken> operands)
    {
        switch (op)
        {
            // ---------- graphics state ----------
            case "q":
                _ctmStack.Push(_ctm);
                Emit("q");
                break;
            case "Q":
                if (_ctmStack.Count > 0) _ctm = _ctmStack.Pop();
                Emit("Q");
                break;
            case "cm":
                if (operands.Count >= 6)
                {
                    var m = new Matrix3x2(Num(operands, 0), Num(operands, 1), Num(operands, 2),
                                          Num(operands, 3), Num(operands, 4), Num(operands, 5));
                    _ctm = _ctm.Multiply(m);
                }
                EmitOperands(operands); Emit("cm");
                break;
            case "w":
                if (operands.Count == 1) _lineWidth = Num(operands, 0);
                EmitOperands(operands); Emit("w");
                break;

            // ---------- path construction ----------
            case "re":
                if (operands.Count >= 4)
                {
                    var x = Num(operands, 0); var y = Num(operands, 1);
                    var w = Num(operands, 2); var h = Num(operands, 3);
                    AddUserPoint(x, y); AddUserPoint(x + w, y + h);
                }
                EmitOperands(operands); Emit("re");
                break;
            case "m":
                if (operands.Count >= 2) AddUserPoint(Num(operands, 0), Num(operands, 1));
                EmitOperands(operands); Emit("m");
                break;
            case "l":
                if (operands.Count >= 2) AddUserPoint(Num(operands, 0), Num(operands, 1));
                EmitOperands(operands); Emit("l");
                break;
            case "c":
                if (operands.Count >= 6)
                {
                    AddUserPoint(Num(operands, 0), Num(operands, 1));
                    AddUserPoint(Num(operands, 2), Num(operands, 3));
                    AddUserPoint(Num(operands, 4), Num(operands, 5));
                }
                EmitOperands(operands); Emit("c");
                break;
            case "v":
                if (operands.Count >= 4)
                {
                    AddUserPoint(Num(operands, 0), Num(operands, 1));
                    AddUserPoint(Num(operands, 2), Num(operands, 3));
                }
                EmitOperands(operands); Emit("v");
                break;
            case "y":
                if (operands.Count >= 4)
                {
                    AddUserPoint(Num(operands, 0), Num(operands, 1));
                    AddUserPoint(Num(operands, 2), Num(operands, 3));
                }
                EmitOperands(operands); Emit("y");
                break;
            case "h":
                Emit("h");
                break;

            // ---------- path painting: drop fills/strokes inside a zone ----------
            case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*":
                if (PathHitsZone()) { RemovedPathPaints++; ResetPath(); }
                else { ResetPath(); EmitOperands(operands); Emit(op); }
                break;
            case "s": case "S":
                if (PathHitsZone()) { RemovedPathPaints++; ResetPath(); }
                else { ResetPath(); EmitOperands(operands); Emit(op); }
                break;
            case "n":
                ResetPath(); Emit("n");
                break;

            // ---------- images ----------
            case "Do":
                if (operands.Count == 1 && CurrentUnitBoxHitsZone())
                {
                    RemovedImages++;
                    // Swallow the paint; the XObject resource stays unused and is
                    // pruned separately by the document-level pass.
                }
                else { EmitOperands(operands); Emit("Do"); }
                break;

            // ---------- text objects ----------
            case "BT":
                _text.BeginTextObject();
                _text.Ctm = _ctm;
                Emit("BT");
                break;
            case "ET":
                _text.EndTextObject();
                Emit("ET");
                break;
            case "Tm":
                if (operands.Count >= 6)
                {
                    _text.Ctm = _ctm;
                    _text.SetTextMatrix(new Matrix3x2(
                        Num(operands, 0), Num(operands, 1), Num(operands, 2),
                        Num(operands, 3), Num(operands, 4), Num(operands, 5)));
                }
                EmitOperands(operands); Emit("Tm");
                break;
            case "Td":
                if (operands.Count >= 2) _text.NewLine(Num(operands, 0), Num(operands, 1));
                EmitOperands(operands); Emit("Td");
                break;
            case "TD":
                if (operands.Count >= 2) _text.NewLineWithLeading(Num(operands, 0), Num(operands, 1));
                EmitOperands(operands); Emit("TD");
                break;
            case "T*":
                _text.NewLine(0, -_text.Leading);
                Emit("T*");
                break;
            case "Tf":
                if (operands.Count >= 2) _text.FontSize = Num(operands, 1);
                EmitOperands(operands); Emit("Tf");
                break;
            case "Tc":
                if (operands.Count == 1) _text.CharSpacing = Num(operands, 0);
                EmitOperands(operands); Emit("Tc");
                break;
            case "Tw":
                if (operands.Count == 1) _text.WordSpacing = Num(operands, 0);
                EmitOperands(operands); Emit("Tw");
                break;
            case "Tz":
                if (operands.Count == 1) _text.HorizontalScaling = Num(operands, 0);
                EmitOperands(operands); Emit("Tz");
                break;
            case "Ts":
                if (operands.Count == 1) _text.Rise = Num(operands, 0);
                EmitOperands(operands); Emit("Ts");
                break;
            case "TL":
                if (operands.Count == 1) _text.Leading = Num(operands, 0);
                EmitOperands(operands); Emit("TL");
                break;

            // ---------- text showing: the actual glyph removal ----------
            case "Tj":
            case "'":
                RewriteSimpleShow(operands, op);
                break;
            case "\"":
                // aw ac string "
                RewriteSimpleShow(operands, op, skipLeading: 2);
                break;
            case "TJ":
                RewriteArrayShow(operands);
                break;

            default:
                // Unknown operator: pass through untouched to stay forward-compatible.
                EmitOperands(operands);
                Emit(op);
                break;
        }
    }

    private void RewriteSimpleShow(List<ContentToken> operands, string op, int skipLeading = 0)
    {
        if (operands.Count == 0) return;
        var stateOperands = operands.Take(skipLeading).ToList();
        var stringToken = operands.Skip(skipLeading).FirstOrDefault(t =>
            t.Kind is ContentTokenKind.LiteralString or ContentTokenKind.HexString);
        if (stringToken == null)
        {
            EmitOperands(operands); Emit(op);
            return;
        }

        var pieces = SplitStringByZone(stringToken.Raw);
        if (pieces.All(p => p.Keep))
        {
            EmitOperands(stateOperands);
            Emit(RenderToken(stringToken)); Emit(op);
            return;
        }

        RemovedGlyphRuns++;
        // Any leading state operands (aw/ac for the " operator) still apply first.
        EmitOperands(stateOperands);
        Emit(BuildTJArray(stringToken.Kind, pieces));
    }

    private void RewriteArrayShow(List<ContentToken> operands)
    {
        if (operands.Count == 0) { Emit("[] TJ"); return; }

        // Rebuild the TJ array piece by piece, consuming glyphs in extraction
        // order. Non-string numbers (original kerning) are preserved; removed
        // glyph runs are replaced by an equivalent negative displacement so the
        // surviving glyphs keep their exact positions.
        var sb = new StringBuilder();
        sb.Append("[ ");
        var anyRemoved = false;
        var pendingGap = 0.0;
        ContentTokenKind stringKind = ContentTokenKind.LiteralString;

        foreach (var t in operands)
        {
            if (t.Kind is ContentTokenKind.Real or ContentTokenKind.Integer)
            {
                pendingGap += t.AsDouble();
                continue;
            }

            if (t.Kind is not (ContentTokenKind.LiteralString or ContentTokenKind.HexString))
                continue;

            stringKind = t.Kind;
            var pieces = SplitStringByZone(t.Raw);
            if (pieces.All(p => p.Keep))
            {
                if (pendingGap != 0) { sb.Append(NumStr(pendingGap)).Append(' '); pendingGap = 0; }
                sb.Append(RenderRawString(t.Kind, t.Raw)).Append(' ');
                continue;
            }

            anyRemoved = true;
            RemovedGlyphRuns++;
            foreach (var piece in pieces)
            {
                if (piece.Keep)
                {
                    if (pendingGap != 0) { sb.Append(NumStr(pendingGap)).Append(' '); pendingGap = 0; }
                    sb.Append(RenderRawString(stringKind, piece.Bytes)).Append(' ');
                }
                else
                {
                    pendingGap += piece.RemovedWidthTextSpace;
                }
            }
        }

        if (pendingGap != 0) sb.Append(NumStr(pendingGap)).Append(' ');
        sb.Append("] TJ");

        if (!anyRemoved)
        {
            EmitOperands(operands); Emit("TJ");
        }
        else
        {
            Emit(sb.ToString());
        }
    }

    /// <summary>
    /// Renders kept string runs and removed-run gaps as one TJ array. The text
    /// matrix is never mutated, so later lines and duplicate-show operators stay
    /// aligned (a Td-based approach shifted the line and caused duplicated text).
    /// </summary>
    private string BuildTJArray(ContentTokenKind kind, List<GlyphPiece> pieces)
    {
        var sb = new StringBuilder("[ ");
        var pendingGap = 0.0;
        foreach (var piece in pieces)
        {
            if (piece.Keep)
            {
                if (pendingGap != 0) { sb.Append(NumStr(pendingGap)).Append(' '); pendingGap = 0; }
                sb.Append(RenderRawString(kind, piece.Bytes)).Append(' ');
            }
            else
            {
                pendingGap += piece.RemovedWidthTextSpace;
            }
        }
        if (pendingGap != 0) sb.Append(NumStr(pendingGap)).Append(' ');
        sb.Append("] TJ");
        return sb.ToString();
    }

    /// <summary>
    /// Walks the raw bytes of one show-string. The first glyph is located by its
    /// user-space origin (robust across multiple content streams); subsequent
    /// glyphs follow in reading order. Result is grouped into keep/remove runs.
    /// One PdfPig glyph may decode from several raw bytes or several characters,
    /// so the character count is reconciled before the next glyph is consumed.
    /// </summary>
    private List<GlyphPiece> SplitStringByZone(byte[] raw)
    {
        var pieces = new List<GlyphPiece>();
        var current = new List<byte>();
        var currentKeep = true;
        double removedWidthText = 0;
        var charsAhead = 0;
        var firstGlyph = true;

        void Flush()
        {
            if (current.Count == 0) return;
            pieces.Add(new GlyphPiece(currentKeep, current.ToArray(), removedWidthText));
            current.Clear();
            removedWidthText = 0;
        }

        for (var pos = 0; pos < raw.Length; pos++)
        {
            GlyphInfo? glyph = null;
            if (charsAhead == 0)
            {
                if (firstGlyph)
                {
                    // Anchor this show string at the current text origin so a new
                    // content stream cannot re-match glyphs already consumed.
                    var (ux, uy) = _text.TextToUser.Apply(0, 0);
                    glyph = _glyphs.MatchAt(ux, uy);
                    firstGlyph = false;
                }
                else
                {
                    glyph = _glyphs.ConsumeNext();
                }
                charsAhead = Math.Max(1, glyph?.Value.Length ?? 1);
            }
            charsAhead--;

            var keep = glyph == null || !HitsAnyZone(glyph.Bounds);
            if (current.Count > 0 && keep != currentKeep) Flush();
            currentKeep = keep;

            current.Add(raw[pos]);
            if (!keep && glyph != null)
            {
                // TJ displacement is in thousandths of the scaled font size.
                removedWidthText += glyph.Width * 1000.0
                    / Math.Max(_text.FontSize, 0.0001)
                    / Math.Max(_text.FontScaleX, 0.0001);
            }
        }
        Flush();
        if (pieces.Count == 0) pieces.Add(new GlyphPiece(true, Array.Empty<byte>(), 0));
        return pieces;
    }

    private bool HitsAnyZone(PdfRect box) => _zones.Any(z => z.IntersectsWith(box));

    private bool CurrentUnitBoxHitsZone()
    {
        var box = new PdfRect(0, 0, 1, 1).Transform(_ctm);
        return HitsAnyZone(box);
    }

    private void AddUserPoint(double x, double y)
    {
        var (ux, uy) = _ctm.Apply(x, y);
        if (!_pathStarted)
        {
            _pMinX = _pMaxX = ux; _pMinY = _pMaxY = uy;
            _pathStarted = true;
        }
        else
        {
            _pMinX = Math.Min(_pMinX, ux); _pMaxX = Math.Max(_pMaxX, ux);
            _pMinY = Math.Min(_pMinY, uy); _pMaxY = Math.Max(_pMaxY, uy);
        }
        _path.Add((ux, uy));
    }

    private bool PathHitsZone()
    {
        if (!_pathStarted) return false;
        var box = new PdfRect(_pMinX, _pMinY, _pMaxX, _pMaxY);
        return HitsAnyZone(box);
    }

    private void ResetPath()
    {
        _path.Clear();
        _pathStarted = false;
    }

    private static double Num(List<ContentToken> ops, int i) => ops[i].AsDouble();

    private static string NumStr(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string RenderToken(ContentToken t) => t.Kind switch
    {
        ContentTokenKind.LiteralString => RenderRawString(ContentTokenKind.LiteralString, t.Raw),
        ContentTokenKind.HexString => RenderRawString(ContentTokenKind.HexString, t.Raw),
        ContentTokenKind.Name => "/" + Encoding.UTF8.GetString(t.Raw),
        _ => t.Keyword
    };

    private static string RenderRawString(ContentTokenKind kind, byte[] raw)
    {
        if (kind == ContentTokenKind.HexString)
        {
            var hex = new StringBuilder("<");
            foreach (var b in raw) hex.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            return hex.Append('>').ToString();
        }
        var sb = new StringBuilder("(");
        foreach (var b in raw)
        {
            switch (b)
            {
                case (byte)'(': sb.Append("\\("); break;
                case (byte)')': sb.Append("\\)"); break;
                case (byte)'\\': sb.Append("\\\\"); break;
                case (byte)'\r': sb.Append("\\r"); break;
                case (byte)'\n': sb.Append("\\n"); break;
                default: sb.Append((char)b); break;
            }
        }
        return sb.Append(')').ToString();
    }

    private sealed record GlyphPiece(bool Keep, byte[] Bytes, double RemovedWidthTextSpace);
}
