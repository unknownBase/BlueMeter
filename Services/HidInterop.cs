using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BlueMeter.Services;

/// <summary>
/// 只读 HID 互操作。Rapoo 仅读取 input report；LINGBAO K98 只发送固定的只读电量查询命令。
/// 本类不调用 HidD_SetFeature / HidD_SetOutputReport，不修改设备设置。
/// </summary>
internal static class HidInterop
{
    internal const byte RapooBatteryReportId = 0xBB;

    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const int ErrorIoPending = 997;
    private const int ErrorTimeout = 1460;
    private const int ErrorInvalidParameter = 87;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorInsufficientBuffer = 122;
    private const int HidpStatusSuccess = 0x00110000;
    private const int HidStringBufferLength = 512;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;
    private const uint Infinite = 0xFFFFFFFF;

    internal static IReadOnlyList<string> EnumerateHidInterfacePaths()
    {
        var paths = new List<string>();
        HidD_GetHidGuid(out var hidGuid);

        var deviceInfoSet = SetupDiGetClassDevs(
            ref hidGuid,
            null,
            IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);

        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "SetupDiGetClassDevs failed");
        }

        try
        {
            uint index = 0;
            while (true)
            {
                var interfaceData = new SpDeviceInterfaceData
                {
                    Size = Marshal.SizeOf<SpDeviceInterfaceData>(),
                };

                if (!SetupDiEnumDeviceInterfaces(
                        deviceInfoSet,
                        IntPtr.Zero,
                        ref hidGuid,
                        index,
                        ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems)
                    {
                        break;
                    }

                    throw new Win32Exception(error, "SetupDiEnumDeviceInterfaces failed");
                }

                index++;

                if (!SetupDiGetDeviceInterfaceDetail(
                        deviceInfoSet,
                        ref interfaceData,
                        IntPtr.Zero,
                        0,
                        out var requiredSize,
                        IntPtr.Zero) &&
                    Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "SetupDiGetDeviceInterfaceDetail(size) failed");
                }

                if (requiredSize <= 0)
                {
                    continue;
                }

                var detail = Marshal.AllocHGlobal(requiredSize);
                try
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize is 8 on x64 and 6 on x86.
                    Marshal.WriteInt32(detail, 0, IntPtr.Size == 8 ? 8 : 6);

                    if (!SetupDiGetDeviceInterfaceDetail(
                            deviceInfoSet,
                            ref interfaceData,
                            detail,
                            requiredSize,
                            out _,
                            IntPtr.Zero))
                    {
                        continue;
                    }

                    var path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        paths.Add(path);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return paths;
    }

    internal static SafeFileHandle? TryOpenRead(string path, out int error)
    {
        var handle = CreateFile(
            path,
            GenericRead,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (!handle.IsInvalid)
        {
            error = 0;
            return handle;
        }

        error = Marshal.GetLastWin32Error();
        handle.Dispose();
        return null;
    }

    /// <summary>
    /// 仅用于发送固定的只读电量查询命令。句柄以 overlapped 方式打开，
    /// 以便读写都带超时并在取消时中止。
    /// </summary>
    internal static SafeFileHandle? TryOpenReadWrite(string path, out int error)
    {
        var handle = CreateFile(
            path,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOverlapped,
            IntPtr.Zero);

        if (!handle.IsInvalid)
        {
            error = 0;
            return handle;
        }

        error = Marshal.GetLastWin32Error();
        handle.Dispose();
        return null;
    }

    internal static bool TryGetAttributes(
        SafeFileHandle handle,
        out HidAttributes attributes)
    {
        attributes = new HidAttributes
        {
            Size = Marshal.SizeOf<HidAttributes>(),
        };

        return HidD_GetAttributes(handle, ref attributes);
    }

    internal static string? GetManufacturer(SafeFileHandle handle) =>
        GetString(handle, HidStringKind.Manufacturer);

    internal static string? GetProduct(SafeFileHandle handle) =>
        GetString(handle, HidStringKind.Product);

    internal static bool TryGetCaps(SafeFileHandle handle, out HidCaps caps)
    {
        caps = default;

        if (!HidD_GetPreparsedData(handle, out var preparsedData))
        {
            return false;
        }

        try
        {
            var nativeCaps = new NativeHidpCaps
            {
                Reserved = new ushort[17],
            };

            if (HidP_GetCaps(preparsedData, ref nativeCaps) != HidpStatusSuccess)
            {
                return false;
            }

            caps = new HidCaps(
                nativeCaps.UsagePage,
                nativeCaps.Usage,
                nativeCaps.InputReportByteLength,
                nativeCaps.OutputReportByteLength,
                nativeCaps.FeatureReportByteLength);
            return true;
        }
        finally
        {
            HidD_FreePreparsedData(preparsedData);
        }
    }

    internal static bool TryGetInputReport(
        SafeFileHandle handle,
        byte reportId,
        byte[] buffer)
    {
        if (buffer.Length == 0)
        {
            return false;
        }

        buffer[0] = reportId;
        return HidD_GetInputReport(handle, buffer, buffer.Length);
    }

    /// <summary>
    /// 发送一个固定长度的 output report。只用于 K98 的只读电量查询。
    /// </summary>
    internal static bool TryWriteReport(
        SafeFileHandle handle,
        byte[] report,
        int timeoutMilliseconds,
        CancellationToken cancellationToken,
        out int error)
    {
        error = 0;
        if (report.Length == 0)
        {
            error = ErrorInvalidParameter;
            return false;
        }

        var eventHandle = CreateEventW(IntPtr.Zero, true, false, null);
        if (eventHandle == IntPtr.Zero)
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }

        var bufferHandle = GCHandle.Alloc(report, GCHandleType.Pinned);
        var overlapped = new HidOverlapped { Event = eventHandle };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (WriteFile(
                    handle,
                    bufferHandle.AddrOfPinnedObject(),
                    report.Length,
                    out var written,
                    ref overlapped))
            {
                if (written != report.Length)
                {
                    error = ErrorInvalidParameter;
                    return false;
                }

                return true;
            }

            error = Marshal.GetLastWin32Error();
            if (error != ErrorIoPending)
            {
                return false;
            }

            if (!TryWaitForIo(handle, ref overlapped, timeoutMilliseconds, cancellationToken, out error))
            {
                return false;
            }

            if (!GetOverlappedResult(handle, ref overlapped, out written, false))
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }

            if (written != report.Length)
            {
                error = ErrorInvalidParameter;
                return false;
            }

            return true;
        }
        finally
        {
            bufferHandle.Free();
            CloseHandle(eventHandle);
        }
    }

    /// <summary>
    /// 读取一个 input report。只用于 K98 的只读电量响应。
    /// </summary>
    internal static bool TryReadReport(
        SafeFileHandle handle,
        byte[] buffer,
        int timeoutMilliseconds,
        CancellationToken cancellationToken,
        out int bytesRead,
        out int error)
    {
        bytesRead = 0;
        error = 0;
        if (buffer.Length == 0)
        {
            error = ErrorInvalidParameter;
            return false;
        }

        var eventHandle = CreateEventW(IntPtr.Zero, true, false, null);
        if (eventHandle == IntPtr.Zero)
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }

        var bufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        var overlapped = new HidOverlapped { Event = eventHandle };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ReadFile(
                    handle,
                    bufferHandle.AddrOfPinnedObject(),
                    buffer.Length,
                    out bytesRead,
                    ref overlapped))
            {
                return true;
            }

            error = Marshal.GetLastWin32Error();
            if (error != ErrorIoPending)
            {
                return false;
            }

            if (!TryWaitForIo(handle, ref overlapped, timeoutMilliseconds, cancellationToken, out error))
            {
                return false;
            }

            if (!GetOverlappedResult(handle, ref overlapped, out bytesRead, false))
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }

            return true;
        }
        finally
        {
            bufferHandle.Free();
            CloseHandle(eventHandle);
        }
    }

    private static bool TryWaitForIo(
        SafeFileHandle handle,
        ref HidOverlapped overlapped,
        int timeoutMilliseconds,
        CancellationToken cancellationToken,
        out int error)
    {
        error = 0;
        var started = Environment.TickCount64;

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                CancelIoEx(handle, ref overlapped);
                WaitForSingleObject(overlapped.Event, Infinite);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var remaining = timeoutMilliseconds - (int)(Environment.TickCount64 - started);
            if (remaining <= 0)
            {
                CancelIoEx(handle, ref overlapped);
                WaitForSingleObject(overlapped.Event, Infinite);
                error = ErrorTimeout;
                return false;
            }

            var wait = WaitForSingleObject(overlapped.Event, (uint)Math.Min(remaining, 50));
            if (wait == WaitObject0)
            {
                return true;
            }

            if (wait == WaitTimeout)
            {
                continue;
            }

            error = Marshal.GetLastWin32Error();
            return false;
        }
    }

    private static string? GetString(SafeFileHandle handle, HidStringKind kind)
    {
        var buffer = new StringBuilder(HidStringBufferLength);
        var success = kind switch
        {
            HidStringKind.Manufacturer => HidD_GetManufacturerString(
                handle,
                buffer,
                buffer.Capacity * 2),
            HidStringKind.Product => HidD_GetProductString(
                handle,
                buffer,
                buffer.Capacity * 2),
            _ => false,
        };

        if (!success)
        {
            return null;
        }

        var text = buffer.ToString().Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    internal readonly record struct HidCaps(
        ushort UsagePage,
        ushort Usage,
        ushort InputReportLength,
        ushort OutputReportLength,
        ushort FeatureReportLength);

    internal struct HidAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidOverlapped
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public int Offset;
        public int OffsetHigh;
        public IntPtr Event;
    }

    private enum HidStringKind
    {
        Manufacturer,
        Product,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeHidpCaps
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

    [DllImport("hid.dll", SetLastError = true)]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(
        SafeFileHandle hidDeviceObject,
        ref HidAttributes attributes);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool HidD_GetManufacturerString(
        SafeFileHandle hidDeviceObject,
        StringBuilder buffer,
        int bufferLength);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool HidD_GetProductString(
        SafeFileHandle hidDeviceObject,
        StringBuilder buffer,
        int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(
        SafeFileHandle hidDeviceObject,
        out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetInputReport(
        SafeFileHandle hidDeviceObject,
        byte[] reportBuffer,
        int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern int HidP_GetCaps(
        IntPtr preparsedData,
        ref NativeHidpCaps capabilities);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize,
        out int requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        SafeFileHandle hFile,
        IntPtr lpBuffer,
        int nNumberOfBytesToWrite,
        out int lpNumberOfBytesWritten,
        ref HidOverlapped lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafeFileHandle hFile,
        IntPtr lpBuffer,
        int nNumberOfBytesToRead,
        out int lpNumberOfBytesRead,
        ref HidOverlapped lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEventW(
        IntPtr lpEventAttributes,
        bool bManualReset,
        bool bInitialState,
        string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(
        IntPtr hHandle,
        uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(
        SafeFileHandle hFile,
        ref HidOverlapped lpOverlapped,
        out int lpNumberOfBytesTransferred,
        bool bWait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(
        SafeFileHandle hFile,
        ref HidOverlapped lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);
}
