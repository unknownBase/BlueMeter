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
    private const int DeadlineSliceMilliseconds = 200;
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
            var startedAt = Environment.TickCount64;
            var budgetDeadline = startedAt + K98RetrySchedule.TotalBudgetMilliseconds;
            var lastWriteError = 0;

            for (var attempt = 0; attempt < K98RetrySchedule.AttemptCount; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Environment.TickCount64 >= budgetDeadline)
                {
                    break;
                }

                if (!HidInterop.TryWriteReport(
                        handle,
                        request,
                        K98RetrySchedule.WriteTimeoutMilliseconds,
                        cancellationToken,
                        out var writeError))
                {
                    lastWriteError = writeError;
                    if (!WaitBeforeRetry(handle, attempt, budgetDeadline, cancellationToken))
                    {
                        break;
                    }

                    continue;
                }

                lastWriteError = 0;

                var result = TryReadBatteryOnce(
                    handle,
                    response,
                    request,
                    attempt,
                    budgetDeadline,
                    cancellationToken);
                if (result is not null)
                {
                    return result;
                }

                if (!WaitBeforeRetry(handle, attempt, budgetDeadline, cancellationToken))
                {
                    break;
                }
            }

            if (lastWriteError != 0)
            {
                return BatteryProbeResult.Unavailable(
                    "HidWrite",
                    $"K98 2.4G 电量查询发送失败（Win32={lastWriteError}，" +
                    $"用时 {(Environment.TickCount64 - startedAt) / 1000.0:0.0} 秒）");
            }

            return BatteryProbeResult.Unavailable(
                "HidRead",
                $"未收到 K98 2.4G 电量响应（{K98RetrySchedule.AttemptCount} 次尝试，" +
                $"用时 {(Environment.TickCount64 - startedAt) / 1000.0:0.0} 秒）");
        }
    }

    /// <summary>
    /// 单次尝试：发送查询后按切片读取，直到本尝试截止。命中返回结果，
    /// 超时返回 null 交给下一次尝试；非超时读错误直接返回失败，避免掩盖真实错误。
    /// 首条查询可能被接收器丢弃，因此在本尝试内补发一次（只补发一次）。
    /// </summary>
    private static BatteryProbeResult? TryReadBatteryOnce(
        SafeFileHandle handle,
        byte[] response,
        byte[] request,
        int attempt,
        long budgetDeadline,
        CancellationToken cancellationToken)
    {
        var now = Environment.TickCount64;
        var budgetRemaining = (int)Math.Max(0, budgetDeadline - now);
        var deadline = now + Math.Min(
            K98RetrySchedule.ReadTimeoutMilliseconds(attempt),
            budgetRemaining);
        var nextResendAt = now + K98RetrySchedule.QuickResendIntervalMilliseconds;
        var resendCount = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            now = Environment.TickCount64;
            if (now >= deadline)
            {
                return null;
            }

            if (resendCount < K98RetrySchedule.MaxResendPerAttempt && now >= nextResendAt)
            {
                resendCount++;
                HidInterop.TryWriteReport(
                    handle,
                    request,
                    K98RetrySchedule.WriteTimeoutMilliseconds,
                    cancellationToken,
                    out _);
                nextResendAt = Environment.TickCount64 +
                    K98RetrySchedule.QuickResendIntervalMilliseconds;
            }

            var slice = (int)Math.Min(DeadlineSliceMilliseconds, deadline - now);
            if (!HidInterop.TryReadReport(
                    handle,
                    response,
                    slice,
                    cancellationToken,
                    out var bytesRead,
                    out var readError))
            {
                if (readError == ErrorTimeout)
                {
                    continue;
                }

                return BatteryProbeResult.Unavailable(
                    "HidRead",
                    $"K98 2.4G 电量响应读取失败（Win32={readError}，第 {attempt + 1} 次尝试）");
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

            // 可能是上一条命令的迟到响应或设备主动上报，继续读到本尝试超时。
        }
    }

    /// <summary>
    /// 尝试间退避后发送一次只读版本查询唤醒接收器。返回 false 表示预算已用尽，不再尝试。
    /// </summary>
    private static bool WaitBeforeRetry(
        SafeFileHandle handle,
        int attempt,
        long budgetDeadline,
        CancellationToken cancellationToken)
    {
        // 已经是最后一次尝试，不再退避与唤醒。
        if (attempt + 1 >= K98RetrySchedule.AttemptCount)
        {
            return false;
        }

        var remaining = (int)(budgetDeadline - Environment.TickCount64);
        if (remaining <= 0)
        {
            return false;
        }

        var delay = Math.Min(K98RetrySchedule.RetryDelayMilliseconds(attempt), remaining);
        if (delay > 0 && cancellationToken.WaitHandle.WaitOne(delay))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 设备刚从省电状态恢复时，先发厂商枚举阶段使用的只读版本查询，
        // 帮助接收器唤醒；不解析、不修改任何配置。
        TryWakeUp(handle, budgetDeadline, cancellationToken);
        return Environment.TickCount64 < budgetDeadline;
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
        long budgetDeadline,
        CancellationToken cancellationToken)
    {
        var request = BuildRequest(VersionCommand);
        if (!HidInterop.TryWriteReport(
                handle,
                request,
                K98RetrySchedule.WriteTimeoutMilliseconds,
                cancellationToken,
                out _))
        {
            return;
        }

        var response = new byte[ReportLength];
        var remainingBudget = (int)Math.Max(0, budgetDeadline - Environment.TickCount64);
        var deadline = Environment.TickCount64 + Math.Min(
            K98RetrySchedule.WakeReadTimeoutMilliseconds,
            remainingBudget);
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
