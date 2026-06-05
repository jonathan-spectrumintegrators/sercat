using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;

namespace Sercat;

/// <summary>
/// Lists available serial ports. The authoritative port list comes from
/// <see cref="SerialPort.GetPortNames"/>; friendly descriptions ("USB Serial Port (COM3)")
/// are pulled from SetupAPI via P/Invoke (AOT-safe, no System.Management/WMI dependency).
/// If the SetupAPI enumeration fails for any reason, bare port names are still printed.
/// </summary>
internal static class PortLister
{
    public static void Print(TextWriter output)
    {
        string[] ports = SerialPort.GetPortNames();

        Dictionary<string, string> friendly;
        try
        {
            friendly = GetFriendlyNames();
        }
        catch
        {
            friendly = new Dictionary<string, string>();
        }

        // Union the registry list with anything SetupAPI knew about, in case they differ.
        var all = new SortedSet<string>(ports, StringComparer.OrdinalIgnoreCase);
        foreach (var key in friendly.Keys)
            all.Add(key);

        if (all.Count == 0)
        {
            output.WriteLine("No serial ports found.");
            return;
        }

        foreach (string port in all)
        {
            if (friendly.TryGetValue(port, out string? desc) &&
                !desc.Contains(port, StringComparison.OrdinalIgnoreCase))
            {
                output.WriteLine($"{port}\t{desc}");
            }
            else if (friendly.TryGetValue(port, out desc))
            {
                output.WriteLine(desc); // already contains the COM name, e.g. "USB Serial Port (COM3)"
            }
            else
            {
                output.WriteLine(port);
            }
        }
    }

    // ---- SetupAPI interop ----------------------------------------------------------------

    private static readonly Guid GUID_DEVCLASS_PORTS =
        new("4D36E978-E325-11CE-BFC1-08002BE10318");

    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint SPDRP_FRIENDLYNAME = 0x0000000C;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public UIntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(
        ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property,
        out uint propertyRegDataType, byte[]? propertyBuffer, uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    private static Dictionary<string, string> GetFriendlyNames()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Guid classGuid = GUID_DEVCLASS_PORTS;

        IntPtr hDevInfo = SetupDiGetClassDevsW(ref classGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT);
        if (hDevInfo == INVALID_HANDLE_VALUE)
            return map;

        try
        {
            var buffer = new byte[1024];
            var devInfo = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };

            for (uint index = 0; SetupDiEnumDeviceInfo(hDevInfo, index, ref devInfo); index++)
            {
                if (!SetupDiGetDeviceRegistryPropertyW(
                        hDevInfo, ref devInfo, SPDRP_FRIENDLYNAME,
                        out _, buffer, (uint)buffer.Length, out uint required))
                {
                    continue;
                }

                int len = Math.Min((int)required, buffer.Length);
                string friendly = Encoding.Unicode.GetString(buffer, 0, len).TrimEnd('\0').Trim();
                if (friendly.Length == 0)
                    continue;

                string? port = ExtractComName(friendly);
                if (port != null)
                    map[port] = friendly;
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        return map;
    }

    /// <summary>Extract "COMn" from a friendly name like "USB Serial Port (COM3)".</summary>
    private static string? ExtractComName(string friendly)
    {
        int open = friendly.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
        if (open < 0)
            return null;
        int close = friendly.IndexOf(')', open);
        if (close < 0)
            return null;
        return friendly.Substring(open + 1, close - open - 1); // "COM3"
    }
}
