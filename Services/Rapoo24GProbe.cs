using BlueMeter.Models;

namespace BlueMeter.Services;

/// <summary>
/// Rapoo 2.4G 接收器的只读电量探针。
/// 协议已通过官方 RapooGameDev 和独立只读探测确认：
/// col05 / usage 0xFF00/0x0002 / report 0xBB，电量在 payload 第 6 字节。
/// 本类不会发送 output/feature 报告，也不会修改接收器或鼠标配置。
/// </summary>
public sealed class Rapoo24GProbe
{
    public const byte BatteryReportId = 0xBB;

    private const byte ReportPayloadMarker = 0xB0;
    private const int BatteryOffset = 5;
    private const int BatteryOffsetWithReportId = 6;
    private const int MinimumReportLength = 7;

    public BatteryProbeResult ReadBattery(
        Hid24GDeviceInfo device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        var handle = HidInterop.TryOpenRead(device.InterfacePath, out var openError);
        if (handle is null || handle.IsInvalid)
        {
            handle?.Dispose();
            return BatteryProbeResult.Unavailable(
                "HidOpen",
                $"无法只读打开 Rapoo 2.4G 接口（Win32={openError}）");
        }

        using (handle)
        {
            var bufferLength = Math.Max(device.InputReportLength, MinimumReportLength);
            var buffer = new byte[bufferLength];

            if (!HidInterop.TryGetInputReport(handle, BatteryReportId, buffer))
            {
                var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                return BatteryProbeResult.Unavailable(
                    "HidRead",
                    $"读取 Rapoo 电量报告失败（Win32={error}）");
            }

            if (!TryParseBattery(buffer, out var batteryPercent))
            {
                return BatteryProbeResult.Unavailable(
                    "HidParse",
                    "Rapoo 电量报告格式未识别");
            }

            return BatteryProbeResult.Ok(
                batteryPercent,
                supportsNotifications: false);
        }
    }

    /// <summary>
    /// 兼容两种读取结果：ReadFile 包含 0xBB report id，HidD_GetInputReport 返回
    /// 以 0xB0 开头的 payload。两者电量都位于 payload 的最后有效字节。
    /// </summary>
    internal static bool TryParseBattery(
        ReadOnlySpan<byte> report,
        out int batteryPercent)
    {
        batteryPercent = 0;
        if (report.Length < MinimumReportLength)
        {
            return false;
        }

        var value = report[0] switch
        {
            0xBB => report[BatteryOffsetWithReportId],
            ReportPayloadMarker => report[BatteryOffset],
            _ => (byte)0xFF,
        };

        if (value > 100)
        {
            return false;
        }

        batteryPercent = value;
        return true;
    }
}
