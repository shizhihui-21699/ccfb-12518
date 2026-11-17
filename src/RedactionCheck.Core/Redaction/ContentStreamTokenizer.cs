using System.Globalization;
using System.Text;

namespace RedactionCheck.Core.Redaction;

public enum ContentTokenKind
{
    Integer,
    Real,
    LiteralString,
    HexString,
    Name,
    Keyword,
}

/// <summary>One token from a PDF content stream. Strings retain their raw encoded bytes.</summary>
public sealed class ContentToken
{
    public required ContentTokenKind Kind { get; init; }
    /// <summary>Raw bytes for strings/names; ASCII bytes for numbers/keywords.</summary>
    public required byte[] Raw { get; init; }
    public string Keyword => Encoding.ASCII.GetString(Raw);

    public double AsDouble() => double.Parse(Keyword, CultureInfo.InvariantCulture);
    public int AsInt() => (int)Math.Round(AsDouble());

    public string AsAsciiString()
    {
        var sb = new StringBuilder(Raw.Length);
        foreach (var b in Raw) sb.Append(b >= 32 && b < 127 ? (char)b : '?');
        return sb.ToString();
    }
}

/// <summary>
/// Hand-written lexer for PDF content streams (ISO 32000-1 clause 7.2).
/// Operands are emitted first, then the operator keyword, so callers can
/// process instructions sequentially without look-ahead.
/// </summary>
public static class ContentStreamTokenizer
{
    private static readonly HashSet<byte> Whitespace = new(new[]
    {
        (byte)0, (byte)9, (byte)10, (byte)12, (byte)13, (byte)32
    });

    public static List<ContentToken> Tokenize(byte[] data)
    {
        var tokens = new List<ContentToken>(data.Length / 8);
        int i = 0, n = data.Length;

        while (i < n)
        {
            byte b = data[i];

            if (Whitespace.Contains(b)) { i++; continue; }

            if (b == (byte)'%')
            {
                while (i < n && data[i] != (byte)'\n' && data[i] != (byte)'\r') i++;
                continue;
            }

            if (b == (byte)'(')
            {
                int start = i + 1;
                i = SkipLiteral(data, i);
                tokens.Add(new ContentToken
                {
                    Kind = ContentTokenKind.LiteralString,
                    Raw = Slice(data, start, i - start - 1)
                });
                continue;
            }

            if (b == (byte)'<')
            {
                if (i + 1 < n && data[i + 1] == (byte)'<')
                {
                    tokens.Add(Keyword("<<"));
                    i += 2;
                    continue;
                }
                int start = i + 1;
                i = SkipHex(data, i);
                var hex = Slice(data, start, i - start - 1);
                tokens.Add(new ContentToken { Kind = ContentTokenKind.HexString, Raw = DecodeHex(hex) });
                continue;
            }

            if (b == (byte)'>')
            {
                if (i + 1 < n && data[i + 1] == (byte)'>')
                {
                    tokens.Add(Keyword(">>"));
                    i += 2;
                    continue;
                }
                throw new InvalidDataException($"Unexpected '>' at content stream offset {i}");
            }

            if (b == (byte)'[') { tokens.Add(Keyword("[")); i++; continue; }
            if (b == (byte)']') { tokens.Add(Keyword("]")); i++; continue; }
            if (b == (byte)'{') { tokens.Add(Keyword("{")); i++; continue; }
            if (b == (byte)'}') { tokens.Add(Keyword("}")); i++; continue; }

            if (b == (byte)'/')
            {
                int start = i + 1;
                i++;
                while (i < n && IsRegular(data[i])) i++;
                tokens.Add(new ContentToken
                {
                    Kind = ContentTokenKind.Name,
                    Raw = DecodeName(Slice(data, start, i - start))
                });
                continue;
            }

            if (b == (byte)'+' || b == (byte)'-' || b == (byte)'.' || (b >= (byte)'0' && b <= (byte)'9'))
            {
                int start = i;
                bool real = false;
                if (b == (byte)'+' || b == (byte)'-') i++;
                while (i < n)
                {
                    byte c = data[i];
                    if (c >= (byte)'0' && c <= (byte)'9') { i++; }
                    else if (c == (byte)'.') { real = true; i++; }
                    else break;
                }
                var raw = Slice(data, start, i - start);
                tokens.Add(new ContentToken
                {
                    Kind = real ? ContentTokenKind.Real : ContentTokenKind.Integer,
                    Raw = raw
                });
                continue;
            }

            // Operator keyword: regular, non-digit characters up to delimiter.
            {
                int start = i;
                while (i < n && IsRegular(data[i])) i++;
                if (i == start)
                    throw new InvalidDataException($"Unrecognised byte 0x{b:X2} at content stream offset {i}");
                tokens.Add(Keyword(Encoding.ASCII.GetString(Slice(data, start, i - start))));
            }
        }

        return tokens;
    }

    private static ContentToken Keyword(string text) =>
        new() { Kind = ContentTokenKind.Keyword, Raw = Encoding.ASCII.GetBytes(text) };

    private static bool IsRegular(byte b)
        => !Whitespace.Contains(b) && b is not ((byte)'(' or (byte)')' or (byte)'<' or (byte)'>'
            or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%');

    private static int SkipLiteral(byte[] data, int i)
    {
        // i points at '('
        int depth = 0;
        do
        {
            byte b = data[i];
            if (b == (byte)'\\')
            {
                i += 2; // skip escaped char (line continuations \r\n handled well enough)
                continue;
            }
            if (b == (byte)'(') depth++;
            else if (b == (byte)')') depth--;
            i++;
        } while (i < data.Length && depth > 0);
        return i; // index just past closing ')'
    }

    private static int SkipHex(byte[] data, int i)
    {
        i++; // skip '<'
        while (i < data.Length && data[i] != (byte)'>') i++;
        return Math.Min(i + 1, data.Length);
    }

    private static byte[] Slice(byte[] data, int start, int length)
    {
        var r = new byte[length];
        Array.Copy(data, start, r, 0, length);
        return r;
    }

    private static byte[] DecodeHex(byte[] hex)
    {
        var clean = hex.Where(b => !Whitespace.Contains(b)).ToArray();
        if (clean.Length % 2 == 1)
        {
            var padded = new byte[clean.Length + 1];
            Array.Copy(clean, padded, clean.Length);
            padded[^1] = (byte)'0';
            clean = padded;
        }
        var result = new byte[clean.Length / 2];
        for (int k = 0; k < result.Length; k++)
        {
            int hi = HexNibble(clean[k * 2]);
            int lo = HexNibble(clean[k * 2 + 1]);
            result[k] = (byte)((hi << 4) | lo);
        }
        return result;
    }

    private static int HexNibble(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - '0',
        >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
        _ => 0,
    };

    private static byte[] DecodeName(byte[] raw)
    {
        if (!raw.Contains((byte)'#')) return raw;
        using var ms = new MemoryStream(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == (byte)'#' && i + 2 < raw.Length)
            {
                ms.WriteByte((byte)((HexNibble(raw[i + 1]) << 4) | HexNibble(raw[i + 2])));
                i += 2;
            }
            else ms.WriteByte(raw[i]);
        }
        return ms.ToArray();
    }
}
