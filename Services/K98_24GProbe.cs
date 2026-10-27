using BlueMeter.Models;
using Microsoft.Win32.SafeHandles;

namespace BlueMeter.Services;

/// <summary>
/// LINGBAO K98 2.4G 接收器的只读电量探针。
/// 已通过厂商应用 CDev3632::ReadPower 和独立只读 HID 测试确认：
/// 接口为 MI_01/COL01，usage 0xFF02/0x0002，output/input report 长度均为 20。
/// 请求：13 4A 01 00 ... checksum（前 19 字节字节和）。
/// 响应：13 4A 01 00 02 &lt;battery&gt; &lt;status&gt; ... checksum。
/// 只发送固定查询命令，不写配置、不修改设备设置。
/// </summary>
public sealed class K98_24GProbe
{
    public const byte BatteryReportId = 0x13;
    public const byte PowerCommand = 0x4A;

    private const byte VersionCommand = 0x07;
    private const byte PowerSubCommand = 0x01;
    private const int ReportLength = 20;
    private const int BatteryOffset = 5;
    private const int StatusOffset = 6;
    private const int ChecksumOffset = 19;
    private const int WriteTimeoutMilliseconds = 500;
    private const int ReadTimeoutMilliseconds = 1500;
    private const int WakeReadTimeoutMilliseconds = 1000;
    private const int AttemptCount = 3;
    private const int RetryDelayMilliseconds = 80;
    private const int ErrorTimeout = 1460;

    public BatteryProbeResult ReadBattery(
        Hid24GDeviceInfo device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);

        var handle = HidInterop.TryOpenReadWrite(device.InterfacePath, out var openError);
        if (handle is null || handle.IsInvalid)
        {
            handle?.Dispose();
            return BatteryProbeResult.Unavailable(
                "HidOpen",
                $"无法打开 K98 2.4G 接口（Win32={openError}）");
        }

        using (handle)
        {
            var request = BuildRequest(PowerCommand);
            var response = new byte[ReportLength];

            for (var attempt = 0; attempt < AttemptCount; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!HidInterop.TryWriteReport(
                        handle,
                        request,
                        WriteTimeoutMilliseconds,
                        cancellationToken,
                        out var writeError))
                {
                    if (attempt + 1 < AttemptCount)
                    {
                        cancellationToken.WaitHandle.WaitOne(RetryDelayMilliseconds);
                        continue;
                    }

                    return BatteryProbeResult.Unavailable(
                        "HidWrite",
                        $"K98 2.4G 电量查询发送失败（Win32={writeError}）");
                }

                var deadline = Environment.TickCount64 + ReadTimeoutMilliseconds;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var remaining = (int)(deadline - Environment.TickCount64);
                    if (remaining <= 0)
                    {
                        break;
                    }

                    if (!HidInterop.TryReadReport(
                            handle,
                            response,
                            remaining,
                            cancellationToken,
                            out var bytesRead,
                            out var readError))
                    {
                        if (readError == ErrorTimeout)
                        {
                            break;
                        }

                        return BatteryProbeResult.Unavailable(
                            "HidRead",
                            $"K98 2.4G 电量响应读取失败（Win32={readError}）");
                    }

                    if (TryParseBattery(
                            response.AsSpan(0, bytesRead),
                            out var batteryPercent,
                            out var charging))
                    {
                        return BatteryProbeResult.Ok(
                            batteryPercent,
                            supportsNotifications: false,
                            charging ? "充电中" : null);
                    }

                    // 可能是上一条命令的迟到响应或设备主动上报，继续读到超时。
                }

                if (attempt + 1 < AttemptCount)
                {
                    if (attempt == 0)
                    {
                        // 设备刚上电/刚从省电状态恢复时，先发厂商枚举阶段使用的
                        // 只读版本查询，帮助接收器唤醒；不解析、不修改任何配置。
                        TryWakeUp(handle, cancellationToken);
                    }

                    cancellationToken.WaitHandle.WaitOne(RetryDelayMilliseconds);
                }
            }

            return BatteryProbeResult.Unavailable(
                "HidRead",
                "未收到 K98 2.4G 电量响应");
        }
    }

    /// <summary>
    /// 解析 20 字节原始 input report。battery 位于 offset 5，status 位于 offset 6；
    /// status 高半字节非零表示充电中（与厂商 ReadPower 的判断一致）。
    /// </summary>
    internal static bool TryParseBattery(
        ReadOnlySpan<byte> report,
        out int batteryPercent,
        out bool charging)
    {
        batteryPercent = 0;
        charging = false;

        if (report.Length < ReportLength ||
            report[0] != BatteryReportId ||
            report[1] != PowerCommand ||
            report[2] != PowerSubCommand)
        {
            return false;
        }

        if (report[ChecksumOffset] != ComputeChecksum(report))
        {
            return false;
        }

        var value = report[BatteryOffset];
        if (value > 100)
        {
            return false;
        }

        batteryPercent = value;
        charging = (report[StatusOffset] & 0xF0) != 0;
        return true;
    }

    private static void TryWakeUp(
        SafeFileHandle handle,
        CancellationToken cancellationToken)
    {
        var request = BuildRequest(VersionCommand);
        if (!HidInterop.TryWriteReport(
                handle,
                request,
                WriteTimeoutMilliseconds,
                cancellationToken,
                out _))
        {
            return;
        }

        var response = new byte[ReportLength];
        var deadline = Environment.TickCount64 + WakeReadTimeoutMilliseconds;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = (int)(deadline - Environment.TickCount64);
            if (remaining <= 0)
            {
                return;
            }

            if (!HidInterop.TryReadReport(
                    handle,
                    response,
                    remaining,
                    cancellationToken,
                    out var bytesRead,
                    out _))
            {
                return;
            }

            if (bytesRead >= 3 &&
                response[0] == BatteryReportId &&
                response[1] == VersionCommand)
            {
                return;
            }
        }
    }

    private static byte[] BuildRequest(byte command)
    {
        var report = new byte[ReportLength];
        report[0] = BatteryReportId;
        report[1] = command;
        report[2] = PowerSubCommand;
        report[ChecksumOffset] = ComputeChecksum(report);
        return report;
    }

    private static byte ComputeChecksum(ReadOnlySpan<byte> report)
    {
        var sum = 0;
        for (var i = 0; i < ChecksumOffset; i++)
        {
            sum = (sum + report[i]) & 0xFF;
        }

        return (byte)sum;
    }
}
