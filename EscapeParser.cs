namespace Sercat;

/// <summary>
/// Converts text containing backslash escapes into a raw byte sequence.
///
/// Vocabulary:
///   \xNN   one byte from exactly two hex digits (case-insensitive)
///   \\     a literal backslash (0x5C)
///   \n \r \t \0 \a \b \f \v   the usual C control bytes
///
/// Any other character is emitted as a single byte. Characters are interpreted as
/// Latin-1 (code point == byte value), which keeps the mapping 1:1 and byte-faithful
/// for the whole 0x00-0xFF range. A code point above 0xFF cannot fit in one byte and
/// is reported as an error.
/// </summary>
internal static class EscapeParser
{
    /// <summary>
    /// Parse <paramref name="input"/> into bytes. Returns false (with a human-readable
    /// <paramref name="error"/>) on an invalid or truncated escape, or a non-byte char.
    /// </summary>
    public static bool TryParse(string input, out byte[] bytes, out string? error)
    {
        var buffer = new List<byte>(input.Length);
        error = null;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c != '\\')
            {
                if (c > 0xFF)
                {
                    bytes = Array.Empty<byte>();
                    error = $"character U+{(int)c:X4} at position {i} is not a single byte; " +
                            "use \\xNN to send a specific byte";
                    return false;
                }
                buffer.Add((byte)c);
                continue;
            }

            // We are on a backslash; it must be followed by an escape body.
            if (i + 1 >= input.Length)
            {
                bytes = Array.Empty<byte>();
                error = "trailing backslash with no escape (use \\\\ for a literal backslash)";
                return false;
            }

            char esc = input[++i];
            switch (esc)
            {
                case '\\': buffer.Add((byte)'\\'); break;
                case 'n': buffer.Add(0x0A); break;
                case 'r': buffer.Add(0x0D); break;
                case 't': buffer.Add(0x09); break;
                case '0': buffer.Add(0x00); break;
                case 'a': buffer.Add(0x07); break;
                case 'b': buffer.Add(0x08); break;
                case 'f': buffer.Add(0x0C); break;
                case 'v': buffer.Add(0x0B); break;
                case 'x':
                case 'X':
                    if (i + 2 >= input.Length ||
                        !TryHexDigit(input[i + 1], out int hi) ||
                        !TryHexDigit(input[i + 2], out int lo))
                    {
                        bytes = Array.Empty<byte>();
                        error = $"\\x at position {i - 1} must be followed by exactly two hex digits";
                        return false;
                    }
                    buffer.Add((byte)((hi << 4) | lo));
                    i += 2;
                    break;
                default:
                    bytes = Array.Empty<byte>();
                    error = $"unknown escape '\\{esc}' at position {i - 1}";
                    return false;
            }
        }

        bytes = buffer.ToArray();
        return true;
    }

    private static bool TryHexDigit(char c, out int value)
    {
        if (c >= '0' && c <= '9') { value = c - '0'; return true; }
        if (c >= 'a' && c <= 'f') { value = c - 'a' + 10; return true; }
        if (c >= 'A' && c <= 'F') { value = c - 'A' + 10; return true; }
        value = 0;
        return false;
    }
}
