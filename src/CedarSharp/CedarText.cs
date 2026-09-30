using System.Globalization;
using System.Text;

namespace CedarSharp;

/// <summary>Helpers for Cedar source text: string-literal escapes and identifier paths.</summary>
internal static class CedarText
{
    /// <summary>True when <paramref name="value"/> is a Cedar namespace path such as <c>User</c> or <c>Acme::User</c>.</summary>
    internal static bool IsIdentifierPath(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var start = 0;
        while (true)
        {
            var end = value.IndexOf("::", start, StringComparison.Ordinal);
            var segment = end < 0 ? value.AsSpan(start) : value.AsSpan(start, end - start);
            if (!IsIdentifier(segment)) return false;
            if (end < 0) return true;
            start = end + 2;
            if (start >= value.Length) return false;
        }
    }

    private static bool IsIdentifier(ReadOnlySpan<char> segment)
    {
        if (segment.IsEmpty) return false;
        if (!(char.IsAsciiLetter(segment[0]) || segment[0] == '_')) return false;
        for (var i = 1; i < segment.Length; i++)
            if (!(char.IsAsciiLetterOrDigit(segment[i]) || segment[i] == '_')) return false;
        return true;
    }

    /// <summary>Escapes <paramref name="value"/> into a Cedar string literal body (without the surrounding quotes).</summary>
    internal static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"': builder.Append("\\\""); continue;
                case '\\': builder.Append("\\\\"); continue;
                case '\n': builder.Append("\\n"); continue;
                case '\r': builder.Append("\\r"); continue;
                case '\t': builder.Append("\\t"); continue;
                case '\0': builder.Append("\\0"); continue;
            }
            if (c is >= ' ' and <= '~') { builder.Append(c); continue; }
            int codePoint;
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                codePoint = char.ConvertToUtf32(c, value[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                throw new ArgumentException("String contains an unpaired surrogate that cannot be Cedar-escaped.", nameof(value));
            }
            else
            {
                codePoint = c;
            }
            builder.Append("\\u{").Append(codePoint.ToString("x", CultureInfo.InvariantCulture)).Append('}');
        }
        return builder.ToString();
    }

    /// <summary>Parses a quoted Cedar string literal starting at <paramref name="start"/> (the opening quote). Returns the decoded value and the index just past the closing quote.</summary>
    internal static bool TryUnescapeQuoted(string text, int start, out string value, out int end)
    {
        value = "";
        end = start;
        if (start < 0 || start >= text.Length || text[start] != '"') return false;
        var builder = new StringBuilder();
        var i = start + 1;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '"') { value = builder.ToString(); end = i + 1; return true; }
            if (c == '\\')
            {
                if (i + 1 >= text.Length) return false;
                var escape = text[i + 1];
                switch (escape)
                {
                    case '"': builder.Append('"'); i += 2; continue;
                    case '\\': builder.Append('\\'); i += 2; continue;
                    case '\'': builder.Append('\''); i += 2; continue;
                    case 'n': builder.Append('\n'); i += 2; continue;
                    case 'r': builder.Append('\r'); i += 2; continue;
                    case 't': builder.Append('\t'); i += 2; continue;
                    case '0': builder.Append('\0'); i += 2; continue;
                    case 'u':
                        if (!TryReadUnicodeEscape(text, i + 2, out var codePoint, out var after)) return false;
                        if (codePoint is >= 0xD800 and <= 0xDFFF) return false;
                        builder.Append(char.ConvertFromUtf32(codePoint));
                        i = after;
                        continue;
                    default: return false;
                }
            }
            if (char.IsControl(c)) return false;
            builder.Append(c);
            i++;
        }
        return false;
    }

    private static bool TryReadUnicodeEscape(string text, int start, out int codePoint, out int end)
    {
        codePoint = 0;
        end = start;
        if (start >= text.Length || text[start] != '{') return false;
        var i = start + 1;
        var digits = 0;
        var value = 0;
        while (i < text.Length && text[i] != '}')
        {
            var digit = HexValue(text[i]);
            if (digit < 0 || digits == 6) return false;
            value = (value << 4) | digit;
            digits++;
            i++;
        }
        if (i >= text.Length || digits == 0) return false;
        if (value > 0x10FFFF) return false;
        codePoint = value;
        end = i + 1;
        return true;
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1
    };
}
