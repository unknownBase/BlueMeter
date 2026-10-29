using BlueMeter.Models;
using BlueMeter.Services;
using Xunit;

namespace BlueMeter.Tests;

public sealed class TrayPresenterTests
{
    [Fact]
    public void ApplySnapshots_UpdatesToolTipAndMenu()
    {
        var tray = new FakeTraySurface();
        var settings = new AppSettings { LowBatteryThreshold = 20 };
        var devices = new[] { Snapshot("鼠标", 15) };
        var presenter = new TrayPresenter(tray, () => settings, () => devices);

        presenter.ApplySnapshots(devices, DeviceServiceStatus.Create(DeviceServiceState.Ready, "就绪"));

        Assert.Equal("BlueMeter · 鼠标 15%", tray.ToolTip);
        Assert.True(tray.MenuEnabled);
        Assert.Equal(new[] { "鼠标 · 15% · 电量低" }, tray.MenuHeaders);
    }

    [Fact]
    public void ApplySnapshots_BluetoothDownWithoutHid_PrefersStatusHint()
    {
        var tray = new FakeTraySurface();
        var settings = new AppSettings();
        var devices = new[] { Snapshot("鼠标", 15) };
        var presenter = new TrayPresenter(tray, () => settings, () => devices);

        presenter.ApplySnapshots(
            devices,
            DeviceServiceStatus.Create(DeviceServiceState.NoAdapter, "未发现蓝牙适配器"));

        Assert.Equal("BlueMeter · 未发现蓝牙适配器", tray.ToolTip);
        Assert.Equal(new[] { "鼠标 · 15% · 电量低" }, tray.MenuHeaders);
    }

    [Fact]
    public void ApplySnapshots_BluetoothDownWithHid_KeepsSummary()
    {
        var tray = new FakeTraySurface();
        var settings = new AppSettings();
        var devices = new[]
        {
            Snapshot("键盘", 60, DeviceTransportKind.Hid24G),
        };
        var presenter = new TrayPresenter(tray, () => settings, () => devices);

        presenter.ApplySnapshots(
            devices,
            DeviceServiceStatus.Create(DeviceServiceState.NoAdapter, "未发现蓝牙适配器"));

        Assert.Equal("BlueMeter · 键盘 60%", tray.ToolTip);
    }

    [Fact]
    public void ApplyServiceStatus_ErrorWithoutHid_ShowsHint()
    {
        var tray = new FakeTraySurface();
        var settings = new AppSettings();
        var devices = new[] { Snapshot("鼠标", 15) };
        var presenter = new TrayPresenter(tray, () => settings, () => devices);

        presenter.ApplyServiceStatus(DeviceServiceStatus.Create(DeviceServiceState.Error, "本轮扫描失败"));

        Assert.Equal("BlueMeter · 本轮扫描失败", tray.ToolTip);
    }

    [Fact]
    public void ApplyServiceStatus_WithHid_LeavesToolTipUntouched()
    {
        var tray = new FakeTraySurface();
        var settings = new AppSettings();
        var devices = new[]
        {
            Snapshot("键盘", 60, DeviceTransportKind.Hid24G),
        };
        var presenter = new TrayPresenter(tray, () => settings, () => devices);

        presenter.ApplyServiceStatus(DeviceServiceStatus.Create(DeviceServiceState.Error, "本轮扫描失败"));

        Assert.Null(tray.ToolTip);
    }

    [Fact]
    public void ApplyServiceStatus_Ready_LeavesToolTipUntouched()
    {
        var tray = new FakeTraySurface();
        var settings = new AppSettings();
        var devices = Array.Empty<DeviceBatterySnapshot>();
        var presenter = new TrayPresenter(tray, () => settings, () => devices);

        presenter.ApplyServiceStatus(DeviceServiceStatus.Create(DeviceServiceState.Ready, "就绪"));

        Assert.Null(tray.ToolTip);
    }

    [Fact]
    public void ApplySettings_RespectsMenuSwitch()
    {
        var tray = new FakeTraySurface();
        var settings = new AppSettings { ShowBatteryInTrayMenu = false };
        var devices = new[] { Snapshot("鼠标", 80) };
        var presenter = new TrayPresenter(tray, () => settings, () => devices);

        presenter.ApplySettings();

        Assert.False(tray.MenuEnabled);
    }

    [Fact]
    public void SetRefreshBusy_ForwardsState()
    {
        var tray = new FakeTraySurface();
        var presenter = new TrayPresenter(tray, () => new AppSettings(), Array.Empty<DeviceBatterySnapshot>);

        presenter.SetRefreshBusy(true);
        presenter.SetRefreshBusy(false);

        Assert.Equal(new[] { true, false }, tray.RefreshBusyStates);
    }

    private static DeviceBatterySnapshot Snapshot(
        string name,
        int? percent,
        DeviceTransportKind transport = DeviceTransportKind.BluetoothLe) =>
        BatterySnapshotSelectorTests.Snapshot(name, percent, BatteryReadStatus.Ok, transport);

    private sealed class FakeTraySurface : ITraySurface
    {
        public string? ToolTip { get; private set; }

        public bool MenuEnabled { get; private set; }

        public IReadOnlyList<string> MenuHeaders { get; private set; } = Array.Empty<string>();

        public List<bool> RefreshBusyStates { get; } = new();

        public void SetToolTip(string? text) => ToolTip = text;

        public void SetRefreshBusy(bool busy) => RefreshBusyStates.Add(busy);

        public void SetDeviceBatteryMenu(bool enabled, IReadOnlyList<string> headers)
        {
            MenuEnabled = enabled;
            MenuHeaders = headers;
        }
    }
}
