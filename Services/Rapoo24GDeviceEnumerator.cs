using BlueMeter.Models;

namespace BlueMeter.Services;

/// <summary>
/// 枚举 Rapoo 2.4G 接收器上已确认的只读电量接口。
/// 只匹配 VID_24AE&amp;PID_1861 的 MI_01/COL05，不探测其他厂商或键盘/鼠标输入集合。
/// </summary>
public sealed class Rapoo24GDeviceEnumerator
{
    private const ushort RapooVendorId = 0x24AE;
    private const ushort RapooProductId = 0x1861;
    private const ushort RapooBatteryUsagePage = 0xFF00;
    private const ushort RapooBatteryUsage = 0x0002;
    private const int MinimumBatteryReportLength = 7;

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

                if (!IsRapooBatteryCollection(path))
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
                        $"Rapoo 2.4G 接口读取失败：{ex.GetType().Name} {ex.Message}",
                        "Hid24G");
                }
            }

            AppLog.Debug(
                $"Rapoo 2.4G 接口枚举完成：{devices.Count} 台" +
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
                $"Rapoo 2.4G 接口枚举失败：0x{ex.HResult:X8} {ex.Message}",
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
                $"Rapoo 2.4G 接口只读打开失败：Win32={openError}",
                "Hid24G");
            handle?.Dispose();
            return null;
        }

        using (handle)
        {
            if (!HidInterop.TryGetAttributes(handle, out var attributes) ||
                attributes.VendorId != RapooVendorId ||
                attributes.ProductId != RapooProductId)
            {
                return null;
            }

            if (!HidInterop.TryGetCaps(handle, out var caps) ||
                caps.UsagePage != RapooBatteryUsagePage ||
                caps.Usage != RapooBatteryUsage ||
                caps.InputReportLength < MinimumBatteryReportLength)
            {
                return null;
            }

            var product = HidInterop.GetProduct(handle);
            var manufacturer = HidInterop.GetManufacturer(handle);
            var name = string.IsNullOrWhiteSpace(product)
                ? "Rapoo 2.4G 接收器"
                : product.Trim();

            return new Hid24GDeviceInfo(
                path,
                name,
                manufacturer,
                attributes.VendorId,
                attributes.ProductId,
                path,
                BuildReceiverId(path),
                caps.InputReportLength)
            {
                Protocol = Hid24GProtocol.Rapoo,
            };
        }
    }

    private static bool IsRapooBatteryCollection(string path) =>
        path.Contains("vid_24ae&pid_1861", StringComparison.OrdinalIgnoreCase) &&
        path.Contains("&mi_01&", StringComparison.OrdinalIgnoreCase) &&
        path.Contains("&col05#", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 去掉 collection 后缀，得到同一接收器的稳定身份。
    /// </summary>
    private static string BuildReceiverId(string path)
    {
        var collectionIndex = path.IndexOf("&col", StringComparison.OrdinalIgnoreCase);
        return collectionIndex > 0 ? path[..collectionIndex] : path;
    }
}
