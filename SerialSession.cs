using System.IO.Ports;
using System.Text;

namespace Sercat;

/// <summary>
/// Drives one serial connection: opens/configures the port, streams received bytes to
/// stdout on a background thread, and pumps stdin (or a file) to the port on the main thread.
/// </summary>
internal sealed class SerialSession : IDisposable
{
    private readonly Options _opt;
    private readonly ByteRenderer _renderer;
    private readonly CancellationTokenSource _cts = new();
    private SerialPort? _port;
    private Thread? _rxThread;
    private volatile bool _disconnected;
    private int _shutdownOnce;

    public SerialSession(Options opt, ByteRenderer renderer)
    {
        _opt = opt;
        _renderer = renderer;
    }

    /// <summary>Open the port and run until input ends, the device disconnects, or Ctrl+C.</summary>
    /// <returns>Process exit code.</returns>
    public int Run()
    {
        OpenPort();

        _rxThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "sercat-rx" };
        _rxThread.Start();

        // 1) Optional file send.
        if (_opt.SendFile != null)
        {
            SendFile(_opt.SendFile);
            if (_opt.QuitAfterSend || _cts.IsCancellationRequested)
                return Finish();
        }

        // 2) Then stream stdin (raw if piped, line/cooked if interactive).
        if (!_cts.IsCancellationRequested)
        {
            if (Console.IsInputRedirected)
            {
                PumpRawStdin();
                if (!_opt.QuitAfterSend && !_cts.IsCancellationRequested)
                    _cts.Token.WaitHandle.WaitOne(); // keep showing RX until Ctrl+C
            }
            else
            {
                InteractiveLoop();
            }
        }

        return Finish();
    }

    private void OpenPort()
    {
        var port = new SerialPort(_opt.Port!, _opt.Baud, _opt.Parity, _opt.DataBits, _opt.StopBits)
        {
            Handshake = _opt.Flow,
            ReadTimeout = SerialPort.InfiniteTimeout,
            WriteTimeout = SerialPort.InfiniteTimeout,
            DtrEnable = _opt.Dtr,
            RtsEnable = _opt.Rts,
        };
        port.Open();
        _port = port;
    }

    // ---- receive (port -> stdout) --------------------------------------------------------

    private void ReceiveLoop()
    {
        Stream output = Console.OpenStandardOutput();
        var buffer = new byte[4096];
        Stream input = _port!.BaseStream;

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                int n = input.Read(buffer, 0, buffer.Length);
                if (n <= 0)
                    break; // stream closed
                _renderer.Render(buffer.AsSpan(0, n), output);
            }
        }
        catch (Exception) when (_cts.IsCancellationRequested)
        {
            // Expected: the port was closed during shutdown.
        }
        catch (Exception ex)
        {
            // Unexpected: device unplugged, driver removed, etc.
            _disconnected = true;
            Console.Error.WriteLine($"sercat: device disconnected ({ex.GetType().Name})");
            _cts.Cancel();
        }
    }

    // ---- send (stdin/file -> port) -------------------------------------------------------

    private void InteractiveLoop()
    {
        // The console is in cooked mode: the OS handles echo, backspace and paste, and
        // hands us a full line on Enter. We parse escapes and send; the Enter newline is
        // dropped unless the user asked for one (--append-newline) or wrote \x0D / \x0A.
        while (!_cts.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = Console.ReadLine();
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                break;
            }

            if (line == null)
                break; // Ctrl+Z (EOF)

            byte[] bytes;
            if (_opt.NoParse)
            {
                bytes = Encoding.Latin1.GetBytes(line);
            }
            else if (!EscapeParser.TryParse(line, out bytes, out string? error))
            {
                Console.Error.WriteLine($"sercat: {error}");
                continue; // let the user retype the line
            }

            byte[] payload = WithAppendedNewline(bytes);
            if (!Write(payload))
                break;
        }
    }

    private void PumpRawStdin()
    {
        Stream input = Console.OpenStandardInput();
        var buffer = new byte[4096];
        try
        {
            int n;
            while (!_cts.IsCancellationRequested && (n = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (!Write(buffer.AsSpan(0, n)))
                    break;
            }
        }
        catch (Exception) when (_cts.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private void SendFile(string path)
    {
        byte[] data;
        try
        {
            byte[] raw = File.ReadAllBytes(path);
            if (_opt.Raw)
            {
                data = raw;
            }
            else
            {
                // Interpret the file as Latin-1 text so every byte maps 1:1 to a char,
                // then parse backslash escapes the same way interactive input is parsed.
                string text = Encoding.Latin1.GetString(raw);
                if (!EscapeParser.TryParse(text, out data, out string? error))
                {
                    Console.Error.WriteLine($"sercat: {path}: {error}");
                    _cts.Cancel();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"sercat: cannot read '{path}': {ex.Message}");
            _cts.Cancel();
            return;
        }

        SendWithPacing(data);
    }

    /// <summary>
    /// Write <paramref name="data"/>, optionally throttled. With --tx-delay set, data is
    /// emitted in segments split after each 0x0A so slow devices get time to drain.
    /// </summary>
    private void SendWithPacing(ReadOnlySpan<byte> data)
    {
        if (_opt.TxDelayMs <= 0)
        {
            Write(data);
            return;
        }

        int start = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == 0x0A || i == data.Length - 1)
            {
                if (!Write(data.Slice(start, i - start + 1)))
                    return;
                start = i + 1;
                if (start < data.Length && !_cts.IsCancellationRequested)
                    _cts.Token.WaitHandle.WaitOne(_opt.TxDelayMs);
            }
        }
    }

    private byte[] WithAppendedNewline(byte[] bytes)
    {
        ReadOnlySpan<byte> nl = _opt.AppendNewline switch
        {
            NewlineAppend.Cr => stackalloc byte[] { 0x0D },
            NewlineAppend.Lf => stackalloc byte[] { 0x0A },
            NewlineAppend.CrLf => stackalloc byte[] { 0x0D, 0x0A },
            _ => default,
        };
        if (nl.IsEmpty)
            return bytes;

        var result = new byte[bytes.Length + nl.Length];
        bytes.CopyTo(result, 0);
        nl.CopyTo(result.AsSpan(bytes.Length));
        return result;
    }

    private bool Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return true;
        try
        {
            _port!.BaseStream.Write(data.ToArray(), 0, data.Length);
            return true;
        }
        catch (Exception ex)
        {
            if (!_cts.IsCancellationRequested)
            {
                _disconnected = true;
                Console.Error.WriteLine($"sercat: write failed ({ex.GetType().Name})");
                _cts.Cancel();
            }
            return false;
        }
    }

    // ---- lifecycle -----------------------------------------------------------------------

    /// <summary>Signal shutdown and close the port. Safe to call from any thread, repeatedly.</summary>
    public void Shutdown()
    {
        if (Interlocked.Exchange(ref _shutdownOnce, 1) != 0)
            return;

        _cts.Cancel();
        try { _port?.Dispose(); } catch { /* already gone */ }
    }

    private int Finish()
    {
        Shutdown();
        try { _rxThread?.Join(500); } catch { /* ignore */ }
        return _disconnected ? 1 : 0;
    }

    public void Dispose()
    {
        Shutdown();
        _cts.Dispose();
    }
}
