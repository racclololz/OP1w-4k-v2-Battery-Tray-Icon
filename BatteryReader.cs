using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OP1wBatteryTray;

internal sealed record BatteryReading(int Percent, int? VoltageMillivolts, string Source);

internal static class BatteryReader
{
    private const ushort DongleProductId = 0x1970;
    private const ushort WiredProductId = 0x1984;

    public static BatteryReading? TryRead()
    {
        foreach (var target in EnumerateTargets())
        {
            var reading = TryReadFromPath(target.Path, target.Source);
            if (reading is not null) return reading;
        }

        return null;
    }

    public static IReadOnlyList<string> Diagnose()
    {
        var lines = new List<string>();
        foreach (var target in EnumerateTargets())
        {
            lines.Add($"target: {target.Source}");
            lines.Add($"path: {target.Path}");
            lines.AddRange(DiagnosePath(target.Path));
        }

        if (lines.Count == 0) lines.Add("no matching battery feature interfaces found");
        return lines;
    }

    private static IEnumerable<string> DiagnosePath(string path)
    {
        using var handle = NativeMethods.CreateFile(
            path,
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            yield return $"open failed: {Marshal.GetLastWin32Error()}";
            yield break;
        }

        var command = CreateBatteryCommand();
        if (!NativeMethods.HidD_SetFeature(handle, command, command.Length))
        {
            yield return $"set feature failed: {Marshal.GetLastWin32Error()}";
            yield break;
        }

        yield return "set feature: ok";
        Thread.Sleep(500);

        var response = new byte[64];
        response[0] = 0xA1;
        if (!NativeMethods.HidD_GetFeature(handle, response, response.Length))
        {
            yield return $"get feature failed: {Marshal.GetLastWin32Error()}";
            yield break;
        }

        yield return "get feature: ok";
        yield return $"response[0..20]: {string.Join(" ", response.Take(21).Select(b => b.ToString("X2")))}";
        yield return $"marker: {response[1]:X2}";
        yield return $"percent byte: {response[16]}";
    }

    private static BatteryReading? TryReadFromPath(string path, string source)
    {
        using var handle = NativeMethods.CreateFile(
            path,
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid) return null;

        var command = CreateBatteryCommand();
        if (!NativeMethods.HidD_SetFeature(handle, command, command.Length)) return null;
        Thread.Sleep(500);

        var response = new byte[64];
        response[0] = 0xA1;
        if (!NativeMethods.HidD_GetFeature(handle, response, response.Length)) return null;
        if (response[0] != 0xA1) return null;
        if (response[1] != 0x01 && response[1] != 0x08) return null;

        var percent = response[16];
        if (percent > 100) return null;

        int? voltage = null;
        var rawVoltage = response[17] | (response[18] << 8);
        if (rawVoltage is > 2500 and < 5000) voltage = rawVoltage;

        return new BatteryReading(percent, voltage, source);
    }

    private static byte[] CreateBatteryCommand()
    {
        var command = new byte[64];
        command[0] = 0xA1;
        command[1] = 0xB4;
        return command;
    }

    private static IEnumerable<(string Path, string Source)> EnumerateTargets()
    {
        NativeMethods.HidD_GetHidGuid(out var hidGuid);
        var infoSet = NativeMethods.SetupDiGetClassDevs(
            ref hidGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);

        if (infoSet == IntPtr.Zero || infoSet == new IntPtr(-1)) yield break;

        try
        {
            var candidates = new List<(string Path, ushort ProductId, string Source)>();

            for (uint index = 0; ; index++)
            {
                var data = new NativeMethods.SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = Marshal.SizeOf<NativeMethods.SP_DEVICE_INTERFACE_DATA>()
                };

                if (!NativeMethods.SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidGuid, index, ref data))
                    break;

                NativeMethods.SetupDiGetDeviceInterfaceDetail(infoSet, ref data, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);

                var detail = new NativeMethods.SP_DEVICE_INTERFACE_DETAIL_DATA
                {
                    cbSize = IntPtr.Size == 8 ? 8 : 5
                };

                if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(infoSet, ref data, ref detail, Marshal.SizeOf(detail), out requiredSize, IntPtr.Zero))
                    continue;

                var lower = detail.DevicePath.ToLowerInvariant();
                var productId = lower.Contains("vid_3367&pid_1970")
                    ? DongleProductId
                    : lower.Contains("vid_3367&pid_1984")
                        ? WiredProductId
                        : (ushort)0;

                if (productId == 0) continue;
                if (!IsBatteryFeatureInterface(detail.DevicePath)) continue;

                var source = productId == DongleProductId ? "wireless dock" : "wired mouse";
                candidates.Add((detail.DevicePath, productId, source));
            }

            foreach (var candidate in candidates.OrderBy(c => c.ProductId == DongleProductId ? 0 : 1))
                yield return (candidate.Path, candidate.Source);
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(infoSet);
        }
    }

    private static bool IsBatteryFeatureInterface(string path)
    {
        using var handle = NativeMethods.CreateFile(
            path,
            0,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid) return false;
        if (!NativeMethods.HidD_GetPreparsedData(handle, out var preparsedData)) return false;

        try
        {
            var status = NativeMethods.HidP_GetCaps(preparsedData, out var caps);
            return status == NativeMethods.HIDP_STATUS_SUCCESS
                && caps.UsagePage == 0xFF01
                && caps.Usage == 0x0002
                && caps.FeatureReportByteLength >= 64;
        }
        finally
        {
            NativeMethods.HidD_FreePreparsedData(preparsedData);
        }
    }

    private static class NativeMethods
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;
        public const uint DIGCF_PRESENT = 0x00000002;
        public const uint DIGCF_DEVICEINTERFACE = 0x00000010;
        public const int HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public UIntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct SP_DEVICE_INTERFACE_DETAIL_DATA
        {
            public int cbSize;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
            public string DevicePath;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;

            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")]
        public static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool HidD_SetFeature(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool HidD_GetFeature(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, ref SP_DEVICE_INTERFACE_DETAIL_DATA deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    }
}
