# sercat

**netcat for serial ports.** Everything is set on the command line; the terminal sends
what you type and shows what's received. No GUI, no terminal-library magic — just stdin
and stdout, so it pipes cleanly.

```
sercat COM3 -b 115200
```

## Install / build

Framework-dependent .NET 8 app — needs the [.NET 8 runtime](https://dotnet.microsoft.com/download)
installed (the desktop runtime that ships with most Windows dev setups is fine).

```powershell
dotnet build -c Release        # -> bin\Release\net8.0\sercat.exe
# or, to stage a publish folder:
dotnet publish -c Release       # -> bin\Release\net8.0\publish\sercat.exe
```

Dependencies: [`CommandLineParser`](https://www.nuget.org/packages/CommandLineParser)
(argument parsing) and [`System.IO.Ports`](https://www.nuget.org/packages/System.IO.Ports)
(serial access), both restored automatically.

## Usage

```
sercat [options] PORT
sercat --list-ports
```

### Serial parameters

| Option | Default | Notes |
|--------|---------|-------|
| `PORT` | — | e.g. `COM3` (required unless `--list-ports`) |
| `-b, --baud N` | `9600` | |
| `--data-bits N` | `8` | 5–8 |
| `--parity P` | `none` | `none\|even\|odd\|mark\|space` |
| `--stop-bits N` | `1` | `1\|1.5\|2` |
| `--flow F` | `none` | `none\|xonxoff\|rtscts` |
| `--dtr on\|off` | `on` | opening a port toggles DTR — `--dtr off` avoids resetting Arduinos |
| `--rts on\|off` | `on` | |

### Display (received bytes → stdout)

| Option | Default | Notes |
|--------|---------|-------|
| `-m, --mode M` | `print` | `print\|dots\|escaped\|hex` |
| `--printable RANGE` | `0x20-0x7E` | e.g. `0x20-0x7E` or `32-126` |
| `--force-mode` | off | apply `--mode` even when stdout is a pipe |

- **print** — bytes written verbatim; the terminal interprets control chars.
- **dots** — every nonprintable byte becomes `.`.
- **escaped** — printable bytes verbatim, nonprintable bytes shown as `\xNN`.
- **hex** — every byte shown as `\xNN` regardless of printability.

### Send (keyboard / stdin → serial)

| Option | Default | Notes |
|--------|---------|-------|
| `--send-file PATH` | — | escapes parsed by default |
| `--raw` | off | with `--send-file`: send verbatim bytes, no parsing |
| `-q, --quit` | off | exit after the file (or piped stdin) is sent |
| `--append-newline E` | `none` | append `cr\|lf\|crlf\|none` to each typed line |
| `--tx-delay MS` | `0` | delay between lines/chunks when sending (paces slow devices) |
| `--no-parse` | off | don't parse `\xNN` escapes on interactive input |

### Other

`-l, --list-ports` · `-h, --help` · `--version`

## Sending escaped bytes

Interactive keyboard input is **line-buffered (cooked mode)**: the OS handles echo,
backspace, and paste, and hands sercat a whole line when you press Enter. That line is
parsed for escapes and sent. This makes it easy to paste a value out of a manual.

Escape vocabulary (used for typed input and for `--send-file` parsing):

```
\xNN   one byte from two hex digits      \\   a literal backslash
\n \r \t \0 \a \b \f \v   the usual C control bytes
```

The Enter that submits a line is **not** sent. To send a line ending, write it explicitly
(`\x0D`, `\x0A`) or use `--append-newline`. An invalid escape prints an error and the line
is not sent, so you can retype it.

```
> AT\x0D                 sends: 41 54 0D
> \x02payload\x03        sends: 02 ... 03
```

## Piping

- **Piped stdin** (`type data.bin | sercat COM3`) is forwarded as **raw bytes** — no escape
  parsing, no line buffering — so binary data passes through untouched.
- **Piped stdout** (`sercat COM3 -m hex > log.txt`) defaults to **raw bytes** for binary
  safety, ignoring `--mode` unless you pass `--force-mode`.
- stdin/stdout are opened as binary streams, so there's no CRLF translation or `0x1A`
  end-of-file corruption.

```powershell
type firmware.bin | sercat COM3 -b 115200 -q      # send a file and exit
sercat COM3 > capture.bin                          # capture raw received bytes
```

## Examples

```powershell
sercat --list-ports
sercat COM3 -b 115200                               # interactive, raw display
sercat COM3 -m escaped                              # show control bytes as \xNN
sercat COM3 -m hex                                  # hex-dump every received byte
sercat COM4 --send-file commands.txt --tx-delay 50  # send a script, pacing slow device
sercat COM4 --send-file image.bin --raw -q          # send a binary file verbatim, then quit
```

Press **Ctrl+C** to quit (the port is released cleanly). To send a literal control byte by
hand, type its escape, e.g. `\x03` for Ctrl+C.

## Known limitations

- **RX/TX interleave.** In interactive line mode, bytes received while you're mid-typing
  print into the line you're editing. This is cosmetic and unavoidable without a terminal
  library (a deliberate non-goal here).
- **Byte-oriented.** Multi-byte UTF-8 and bytes ≥ 0x80 are treated as individual
  nonprintable bytes in `dots`/`escaped`/`hex` modes — by design.
- **No auto-reconnect.** If the device is unplugged mid-session, sercat reports the
  disconnect and exits with a nonzero code.
- **Windows / .NET.** Built on `System.IO.Ports`; primary target is Windows.
