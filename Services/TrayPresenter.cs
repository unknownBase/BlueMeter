using BlueMeter.Models;

namespace BlueMeter.Services;

/// <summary>
/// 决定托盘上“显示什么”：ToolTip 文案与右键菜单“设备电量”段。
/// <see cref="TrayIconService"/> 只负责把内容画出来，组合根只负责在正确的时机触发。
/// </summary>
public sealed class TrayPresenter
{
    private readonly ITraySurface _tray;
    private readonly Func<AppSettings> _getSettings;
    private readonly Func<IReadOnlyList<DeviceBatterySnapshot>> _getDevices;

    public TrayPresenter(
        ITraySurface tray,
        Func<AppSettings> getSettings,
        Func<IReadOnlyList<DeviceBatterySnapshot>> getDevices)
    {
        ArgumentNullException.ThrowIfNull(tray);
        ArgumentNullException.ThrowIfNull(getSettings);
        ArgumentNullException.ThrowIfNull(getDevices);

        _tray = tray;
        _getSettings = getSettings;
        _getDevices = getDevices;
    }

    public void ShowScanning() => _tray.SetToolTip(TraySummaryBuilder.Scanning());

    public void SetRefreshBusy(bool busy) => _tray.SetRefreshBusy(busy);

    /// <summary>设置变更后重建菜单段：开关与低电量阈值都可能刚改过。</summary>
    public void ApplySettings() => RefreshBatteryMenu(_getDevices());

    /// <summary>
    /// 每轮扫描结束：先刷新菜单条目，再决定 ToolTip。蓝牙不可用且没有 2.4G 设备时，
    /// 用服务状态文案覆盖，避免显示上一轮的旧摘要。
    /// </summary>
    public void ApplySnapshots(
        IReadOnlyList<DeviceBatterySnapshot> snapshots,
        DeviceServiceStatus? serviceStatus)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        RefreshBatteryMenu(snapshots);

        if (serviceStatus is { State: DeviceServiceState.NoAdapter or DeviceServiceState.BluetoothOff } status &&
            !HasHid24G(snapshots))
        {
            _tray.SetToolTip(TraySummaryBuilder.StatusHint(status.Message));
            return;
        }

        _tray.SetToolTip(TraySummaryBuilder.Build(snapshots));
    }

    public void ApplyServiceStatus(DeviceServiceStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        if (status.State is not (DeviceServiceState.NoAdapter
            or DeviceServiceState.BluetoothOff
            or DeviceServiceState.Error))
        {
            return;
        }

        if (HasHid24G(_getDevices()))
        {
            return;
        }

        _tray.SetToolTip(TraySummaryBuilder.StatusHint(status.Message));
    }

    private void RefreshBatteryMenu(IReadOnlyList<DeviceBatterySnapshot> devices)
    {
        var settings = _getSettings();
        var entries = TrayMenuBuilder.Build(devices, settings.LowBatteryThreshold);
        _tray.SetDeviceBatteryMenu(settings.ShowBatteryInTrayMenu, entries);
    }

    private static bool HasHid24G(IReadOnlyList<DeviceBatterySnapshot> devices) =>
        devices.Any(snapshot => snapshot.Transport == DeviceTransportKind.Hid24G);
}
