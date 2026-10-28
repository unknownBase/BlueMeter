using BlueMeter.Models;

namespace BlueMeter.Services;

/// <summary>
/// 低电量提醒的聚合与发送。同一批告警在短窗口内合并为一条气泡，
/// 避免多台设备同时跌破阈值时反复弹窗；通知开关关闭时静默丢弃。
/// 任何线程都可以调用 <see cref="Notify"/>，实际通知由 <see cref="TrayIconService"/> 编组到 UI 线程。
/// </summary>
public sealed class LowBatteryNotifier : IDisposable
{
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(1);
    private const int MaxDeviceNameLength = 24;

    private readonly Func<bool> _notificationsEnabled;
    private readonly Action<string> _showNotification;
    private readonly object _gate = new();
    private readonly Dictionary<string, LowBatteryNotice> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();

    private bool _flushScheduled;
    private int _disposed;

    public LowBatteryNotifier(Func<bool> notificationsEnabled, Action<string> showNotification)
    {
        _notificationsEnabled = notificationsEnabled ?? throw new ArgumentNullException(nameof(notificationsEnabled));
        _showNotification = showNotification ?? throw new ArgumentNullException(nameof(showNotification));
    }

    /// <summary>
    /// 收集一轮告警并在合并窗口结束后统一发送。同一设备重复触发时以最后一次读数为准。
    /// </summary>
    public void Notify(IReadOnlyList<LowBatteryNotice> notices)
    {
        if (notices.Count == 0 || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var notice in notices)
            {
                _pending[notice.DeviceId] = notice;
            }

            if (_flushScheduled)
            {
                return;
            }

            _flushScheduled = true;
        }

        _ = FlushAfterDelayAsync();
    }

    /// <summary>
    /// 单设备：<c>鼠标电量低：15%</c>；多设备：<c>2 个设备电量低，最低：键盘 12%</c>。
    /// </summary>
    public static string BuildMessage(IReadOnlyList<LowBatteryNotice> notices)
    {
        if (notices.Count == 0)
        {
            return string.Empty;
        }

        var ordered = notices
            .OrderBy(notice => notice.BatteryPercent)
            .ThenBy(notice => notice.DeviceName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (ordered.Count == 1)
        {
            var single = ordered[0];
            return $"{ShortenName(single.DeviceName)}电量低：{single.BatteryPercent}%";
        }

        var lowest = ordered[0];
        return $"{ordered.Count} 个设备电量低，最低：{ShortenName(lowest.DeviceName)} {lowest.BatteryPercent}%";
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _cts.Cancel();
        }
        catch
        {
            // 取消已释放的令牌源不影响退出。
        }

        _cts.Dispose();

        lock (_gate)
        {
            _pending.Clear();
            _flushScheduled = false;
        }
    }

    private async Task FlushAfterDelayAsync()
    {
        try
        {
            await Task.Delay(CoalesceWindow, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        LowBatteryNotice[] batch;
        lock (_gate)
        {
            _flushScheduled = false;
            if (_pending.Count == 0)
            {
                return;
            }

            batch = _pending.Values.ToArray();
            _pending.Clear();
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (!_notificationsEnabled())
        {
            AppLog.Debug(
                $"低电量通知已关闭，丢弃 {batch.Length} 条提醒",
                "Battery");
            return;
        }

        try
        {
            _showNotification(BuildMessage(batch));
        }
        catch (Exception ex)
        {
            // 系统关闭通知或专注助手拦截时只记录，不重试。
            AppLog.Warn("低电量气泡发送失败", "Battery", ex);
        }
    }

    private static string ShortenName(string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "设备" : name.Trim();
        return trimmed.Length <= MaxDeviceNameLength
            ? trimmed
            : string.Concat(trimmed.AsSpan(0, MaxDeviceNameLength - 1), "…");
    }
}
