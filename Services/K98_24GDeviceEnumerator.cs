using BlueMeter.Models;

namespace BlueMeter.Services;

/// <summary>
/// 枚举 LINGBAO K98 2.4G 接收器上已确认的只读电量接口。
/// 只匹配 VID_3554&amp;PID_FA09 的 MI_01/COL01，不探测其他厂商或其他集合。
/// </summary>
public sealed class K98_24GDeviceEnumerator
{
    private const ushort LingbaoVendorId = 0x3554;
    private const ushort K98WirelessProductId = 0xFA09;
    private const ushort K98BatteryUsagePage = 0xFF02;
    private const ushort K98BatteryUsage = 0x0002;
    private const int K98ReportLength = 20;

    public Hid24GEnumerationResult Enumerate(CancellationToken cancellationToken)
    {
        var devices = new List<Hid24GDeviceInfo>();
        var matchingInterfaceCount = 0;
        var failedInterfaceCount = 0;

        try
        {
            var paths = HidInterop.EnumerateHidInterfacePaths();
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsK98BatteryCollection(path))
                {
                    continue;
                }

                matchingInterfaceCount++;

                try
                {
                    var device = TryCreateDevice(path);
                    if (device is null)
                    {
                        failedInterfaceCount++;
                    }
                    else if (devices.All(existing => !string.Equals(
                                 existing.InterfacePath,
                                 device.InterfacePath,
                                 StringComparison.OrdinalIgnoreCase)))
                    {
                        devices.Add(device);
                    }
                }
                catch (Exception ex)
                {
                    failedInterfaceCount++;
                    AppLog.Debug(
                        $"LINGBAO K98 2.4G 接口读取失败：{ex.GetType().Name} {ex.Message}",
                        "Hid24G");
                }
            }

            AppLog.Debug(
                $"LINGBAO K98 2.4G 接口枚举完成：{devices.Count} 台" +
                (failedInterfaceCount > 0
                    ? $"，{failedInterfaceCount} 个接口暂不可用"
                    : string.Empty),
                "Hid24G");
            return new Hid24GEnumerationResult(
                devices,
                true,
                null,
                matchingInterfaceCount,
                failedInterfaceCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Warn(
                $"LINGBAO K98 2.4G 接口枚举失败：0x{ex.HResult:X8} {ex.Message}",
                "Hid24G");
            return new Hid24GEnumerationResult(
                Array.Empty<Hid24GDeviceInfo>(),
                false,
                ex.Message);
        }
    }

    private static Hid24GDeviceInfo? TryCreateDevice(string path)
    {
        var handle = HidInterop.TryOpenRead(path, out var openError);
        if (handle is null || handle.IsInvalid)
        {
            AppLog.Debug(
                $"LINGBAO K98 2.4G 接口只读打开失败：Win32={openError}",
                "Hid24G");
            handle?.Dispose();
            return null;
        }

        using (handle)
        {
            if (!HidInterop.TryGetAttributes(handle, out var attributes) ||
                attributes.VendorId != LingbaoVendorId ||
                attributes.ProductId != K98WirelessProductId)
            {
                return null;
            }

            if (!HidInterop.TryGetCaps(handle, out var caps) ||
                caps.UsagePage != K98BatteryUsagePage ||
                caps.Usage != K98BatteryUsage ||
                caps.InputReportLength < K98ReportLength ||
                caps.OutputReportLength < K98ReportLength)
            {
                return null;
            }

            return new Hid24GDeviceInfo(
                path,
                "LINGBAO K98 2.4G",
                "LINGBAO",
                attributes.VendorId,
                attributes.ProductId,
                path,
                BuildReceiverId(path),
                caps.InputReportLength)
            {
                Protocol = Hid24GProtocol.LingbaoK98,
            };
        }
    }

    private static bool IsK98BatteryCollection(string path) =>
        path.Contains("vid_3554&pid_fa09", StringComparison.OrdinalIgnoreCase) &&
        path.Contains("&mi_01&", StringComparison.OrdinalIgnoreCase) &&
        path.Contains("&col01#", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 去掉 collection 后缀，得到同一接收器的稳定身份。
    /// </summary>
    private static string BuildReceiverId(string path)
    {
        var collectionIndex = path.IndexOf("&col", StringComparison.OrdinalIgnoreCase);
        return collectionIndex > 0 ? path[..collectionIndex] : path;
    }
}
