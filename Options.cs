using System.IO.Ports;
using CommandLine;

namespace Sercat;

internal enum NewlineAppend { None, Cr, Lf, CrLf }

internal enum OnOff { On, Off }

/// <summary>
/// The command line as bound by CommandLineParser. Enum-typed options (parity, mode,
/// append-newline, dtr/rts) are parsed case-insensitively; the few options whose accepted
/// spellings don't match a .NET enum (stop-bits, flow, printable range) are taken as
/// strings and converted in <see cref="Options.FromCli"/>.
/// </summary>
internal sealed class CliOptions
{
    [Value(0, MetaName = "port",
        HelpText = "Serial port, e.g. COM3 (required unless --list-ports).")]
    public string? Port { get; set; }

    // Serial parameters
    [Option('b', "baud", Default = 9600, HelpText = "Baud rate.")]
    public int Baud { get; set; }

    [Option("data-bits", Default = 8, HelpText = "Data bits: 5, 6, 7 or 8.")]
    public int DataBits { get; set; }

    [Option("parity", Default = Parity.None, HelpText = "none | even | odd | mark | space.")]
    public Parity Parity { get; set; }

    [Option("stop-bits", Default = "1", HelpText = "Stop bits: 1 | 1.5 | 2.")]
    public string StopBits { get; set; } = "1";

    [Option("flow", Default = "none", HelpText = "Flow control: none | xonxoff | rtscts.")]
    public string Flow { get; set; } = "none";

    [Option("dtr", Default = OnOff.On, HelpText = "Assert DTR on open: on | off.")]
    public OnOff Dtr { get; set; }

    [Option("rts", Default = OnOff.On, HelpText = "Assert RTS on open: on | off.")]
    public OnOff Rts { get; set; }

    // Display
    [Option('m', "mode", Default = DisplayMode.Print,
        HelpText = "Received-byte display: print | dots | escaped | hex.")]
    public DisplayMode Mode { get; set; }

    [Option("printable", Default = "0x20-0x7E",
        HelpText = "Printable byte range, e.g. 0x20-0x7E or 32-126.")]
    public string Printable { get; set; } = "0x20-0x7E";

    [Option("force-mode", HelpText = "Apply --mode even when stdout is redirected to a pipe.")]
    public bool ForceMode { get; set; }

    // Send
    [Option("send-file", HelpText = "Send a file (escapes parsed unless --raw).")]
    public string? SendFile { get; set; }

    [Option("raw", HelpText = "With --send-file: send verbatim bytes, no escape parsing.")]
    public bool Raw { get; set; }

    [Option('q', "quit", HelpText = "Exit after the file (or piped stdin) is sent.")]
    public bool QuitAfterSend { get; set; }

    [Option("append-newline", Default = NewlineAppend.None,
        HelpText = "Append to each typed line: none | cr | lf | crlf.")]
    public NewlineAppend AppendNewline { get; set; }

    [Option("tx-delay", Default = 0,
        HelpText = "Milliseconds between lines/chunks when sending (paces slow devices).")]
    public int TxDelayMs { get; set; }

    [Option("no-parse", HelpText = "Do not parse \\xNN escapes on interactive input.")]
    public bool NoParse { get; set; }

    // Action
    [Option('l', "list-ports", HelpText = "List available ports and exit.")]
    public bool ListPorts { get; set; }
}

/// <summary>
/// Validated, runtime-ready configuration consumed by <see cref="SerialSession"/> and
/// <see cref="ByteRenderer"/>. Produced from a <see cref="CliOptions"/> by <see cref="FromCli"/>.
/// </summary>
internal sealed class Options
{
    public string? Port;
    public int Baud;
    public int DataBits;
    public Parity Parity;
    public StopBits StopBits;
    public Handshake Flow;
    public bool Dtr;
    public bool Rts;

    public DisplayMode Mode;
    public bool ForceMode;
    public byte PrintableMin;
    public byte PrintableMax;

    public string? SendFile;
    public bool Raw;
    public bool QuitAfterSend;
    public NewlineAppend AppendNewline;
    public int TxDelayMs;
    public bool NoParse;

    /// <summary>
    /// Convert and validate parsed CLI input. Returns false with a human-readable
    /// <paramref name="error"/> for any value the parser couldn't reject on its own
    /// (ranges, stop-bits/flow spellings, printable range).
    /// </summary>
    public static bool FromCli(CliOptions cli, out Options? options, out string? error)
    {
        options = null;
        error = null;

        if (cli.Baud <= 0)
            return Fail(out error, "--baud must be a positive integer");
        if (cli.DataBits is < 5 or > 8)
            return Fail(out error, "--data-bits must be 5, 6, 7 or 8");
        if (cli.TxDelayMs < 0)
            return Fail(out error, "--tx-delay must be a non-negative integer (milliseconds)");

        if (!TryStopBits(cli.StopBits, out StopBits stopBits))
            return Fail(out error, "--stop-bits must be 1, 1.5 or 2");
        if (!TryFlow(cli.Flow, out Handshake flow))
            return Fail(out error, "--flow must be none, xonxoff or rtscts");
        if (!TryPrintable(cli.Printable, out byte min, out byte max))
            return Fail(out error, "--printable must be MIN-MAX (e.g. 0x20-0x7E or 32-126) with MIN <= MAX");

        options = new Options
        {
            Port = cli.Port,
            Baud = cli.Baud,
            DataBits = cli.DataBits,
            Parity = cli.Parity,
            StopBits = stopBits,
            Flow = flow,
            Dtr = cli.Dtr == OnOff.On,
            Rts = cli.Rts == OnOff.On,
            Mode = cli.Mode,
            ForceMode = cli.ForceMode,
            PrintableMin = min,
            PrintableMax = max,
            SendFile = cli.SendFile,
            Raw = cli.Raw,
            QuitAfterSend = cli.QuitAfterSend,
            AppendNewline = cli.AppendNewline,
            TxDelayMs = cli.TxDelayMs,
            NoParse = cli.NoParse,
        };
        return true;
    }

    private static bool Fail(out string? error, string message)
    {
        error = message;
        return false;
    }

    private static bool TryStopBits(string s, out StopBits value)
    {
        switch (s)
        {
            case "1": value = System.IO.Ports.StopBits.One; return true;
            case "1.5": value = System.IO.Ports.StopBits.OnePointFive; return true;
            case "2": value = System.IO.Ports.StopBits.Two; return true;
            default: value = System.IO.Ports.StopBits.One; return false;
        }
    }

    private static bool TryFlow(string s, out Handshake value)
    {
        switch (s.ToLowerInvariant())
        {
            case "none": value = Handshake.None; return true;
            case "xonxoff": value = Handshake.XOnXOff; return true;
            case "rtscts": value = Handshake.RequestToSend; return true;
            default: value = Handshake.None; return false;
        }
    }

    private static bool TryPrintable(string s, out byte min, out byte max)
    {
        min = 0;
        max = 0;
        string[] parts = s.Split('-');
        if (parts.Length != 2 || !TryByte(parts[0], out min) || !TryByte(parts[1], out max) || min > max)
            return false;
        return true;
    }

    private static bool TryByte(string s, out byte value)
    {
        s = s.Trim();
        int parsed;
        bool ok = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(s.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out parsed)
            : int.TryParse(s, out parsed);
        if (ok && parsed is >= 0 and <= 0xFF) { value = (byte)parsed; return true; }
        value = 0;
        return false;
    }
}
