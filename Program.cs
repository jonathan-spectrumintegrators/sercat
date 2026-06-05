using CommandLine;

namespace Sercat;

internal static class Program
{
    private static int Main(string[] args)
    {
        // Default settings, but match enum values case-insensitively so "--mode hex" and
        // "--parity even" work as users expect. Help/version/error text is written to
        // stderr by the parser automatically (AutoHelp/AutoVersion).
        using var parser = new Parser(s =>
        {
            s.CaseInsensitiveEnumValues = true;
            s.HelpWriter = Console.Error;
        });

        return parser.ParseArguments<CliOptions>(args)
            .MapResult(Run, HandleParseErrors);
    }

    private static int HandleParseErrors(IEnumerable<Error> errors)
    {
        // --help / --version are surfaced as "errors"; the parser already printed them.
        if (errors.All(e => e.Tag is ErrorType.HelpRequestedError or ErrorType.VersionRequestedError))
            return 0;
        // Any genuine parse error has likewise already been printed with usage.
        return 2;
    }

    private static int Run(CliOptions cli)
    {
        if (cli.ListPorts)
        {
            PortLister.Print(Console.Out);
            return 0;
        }

        if (!Options.FromCli(cli, out Options? opt, out string? error))
        {
            Console.Error.WriteLine($"sercat: {error}");
            return 2;
        }

        if (opt!.Port == null)
        {
            Console.Error.WriteLine(
                "sercat: no serial port specified (e.g. COM3). Use --list-ports to see available ports.");
            return 2;
        }

        // When stdout is redirected to a pipe/file, forward raw bytes (binary-safe) unless
        // the user explicitly forces a display mode with --force-mode.
        DisplayMode effective = (Console.IsOutputRedirected && !opt.ForceMode)
            ? DisplayMode.Print
            : opt.Mode;

        var renderer = new ByteRenderer(effective, opt.PrintableMin, opt.PrintableMax);
        using var session = new SerialSession(opt, renderer);

        // Ctrl+C: release the port, then let the runtime terminate the process.
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = false;
            session.Shutdown();
        };

        try
        {
            return session.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"sercat: {DescribeOpenError(opt.Port!, ex)}");
            return 1;
        }
    }

    private static string DescribeOpenError(string port, Exception ex) => ex switch
    {
        UnauthorizedAccessException =>
            $"{port}: access denied — the port is in use by another program or you lack permission",
        FileNotFoundException =>
            $"{port}: no such port (use --list-ports to see what's available)",
        ArgumentException =>
            $"{port}: invalid port name or parameters ({ex.Message})",
        _ => $"{port}: {ex.Message}",
    };
}
