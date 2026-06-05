using System.Text;

namespace Sercat;

internal enum DisplayMode
{
    /// <summary>Write received bytes verbatim; the terminal interprets control chars.</summary>
    Print,
    /// <summary>Printable bytes verbatim; every nonprintable byte becomes '.'.</summary>
    Dots,
    /// <summary>Printable bytes verbatim; every nonprintable byte becomes \xNN.</summary>
    Escaped,
    /// <summary>Every byte rendered as \xNN regardless of printability.</summary>
    Hex,
}

/// <summary>
/// Renders received bytes to the (binary) output stream according to a <see cref="DisplayMode"/>.
/// A byte is "printable" when it falls within the inclusive [<see cref="PrintableMin"/>,
/// <see cref="PrintableMax"/>] range (default 0x20-0x7E). Bytes >= 0x80 are nonprintable by
/// default — this is a byte-oriented tool, so multi-byte UTF-8 is shown byte-by-byte.
/// </summary>
internal sealed class ByteRenderer
{
    private static readonly char[] HexDigits = "0123456789ABCDEF".ToCharArray();

    public DisplayMode Mode { get; }
    public byte PrintableMin { get; }
    public byte PrintableMax { get; }

    public ByteRenderer(DisplayMode mode, byte printableMin = 0x20, byte printableMax = 0x7E)
    {
        Mode = mode;
        PrintableMin = printableMin;
        PrintableMax = printableMax;
    }

    private bool IsPrintable(byte b) => b >= PrintableMin && b <= PrintableMax;

    /// <summary>
    /// Render <paramref name="data"/> to <paramref name="output"/>. In Print mode the bytes
    /// are forwarded verbatim (binary-safe); other modes emit ASCII text.
    /// </summary>
    public void Render(ReadOnlySpan<byte> data, Stream output)
    {
        if (data.IsEmpty)
            return;

        if (Mode == DisplayMode.Print)
        {
            output.Write(data);
            output.Flush();
            return;
        }

        // Worst case is 4 ASCII chars ("\xNN") per input byte.
        var sb = new StringBuilder(data.Length * 4);
        foreach (byte b in data)
        {
            switch (Mode)
            {
                case DisplayMode.Dots:
                    sb.Append(IsPrintable(b) ? (char)b : '.');
                    break;
                case DisplayMode.Escaped:
                    if (IsPrintable(b))
                        sb.Append((char)b);
                    else
                        AppendHexEscape(sb, b);
                    break;
                case DisplayMode.Hex:
                    AppendHexEscape(sb, b);
                    break;
            }
        }

        // ASCII is sufficient: output is restricted to 0x20-0x7E plus the literal "\xNN" chars.
        byte[] outBytes = Encoding.ASCII.GetBytes(sb.ToString());
        output.Write(outBytes, 0, outBytes.Length);
        output.Flush();
    }

    private static void AppendHexEscape(StringBuilder sb, byte b)
    {
        sb.Append('\\').Append('x');
        sb.Append(HexDigits[b >> 4]);
        sb.Append(HexDigits[b & 0x0F]);
    }
}
