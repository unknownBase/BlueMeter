using BlueMeter.Models;
using BlueMeter.ViewModels;
using Xunit;

namespace BlueMeter.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void BeforeFirstScan_IsInitialLoading()
    {
        using var viewModel = new MainViewModel();

        Assert.True(viewModel.IsInitialLoading);
        Assert.False(viewModel.AreAllUnreachable);
        Assert.False(viewModel.ShowDeviceList);
    }

    [Fact]
    public void AllDevicesUnreachable_ShowsUnreachableStateInsteadOfList()
    {
        using var viewModel = new MainViewModel();

        viewModel.ApplySnapshots(new[]
        {
            Snapshot("Rapoo Gaming Device", BatteryReadStatus.Stale, 68, DeviceTransportKind.Hid24G, Hid24GProtocol.Rapoo),
            Snapshot("LINGBAO K98", BatteryReadStatus.Unavailable, null, DeviceTransportKind.Hid24G, Hid24GProtocol.LingbaoK98),
        });

        Assert.True(viewModel.HasDevices);
        Assert.True(viewModel.AreAllUnreachable);
        Assert.False(viewModel.ShowDeviceList);
        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.IsInitialLoading);
    }

    [Fact]
    public void OneReadableDevice_KeepsListVisible()
    {
        using var viewModel = new MainViewModel();

        viewModel.ApplySnapshots(new[]
        {
            Snapshot("BLE Mouse", BatteryReadStatus.Ok, 85),
            Snapshot("BLE Keyboard", BatteryReadStatus.Unavailable, null),
        });

        Assert.False(viewModel.AreAllUnreachable);
        Assert.True(viewModel.ShowDeviceList);
    }

    [Fact]
    public void UnsupportedDevice_IsNotTreatedAsUnreachable()
    {
        using var viewModel = new MainViewModel();

        viewModel.ApplySnapshots(new[]
        {
            Snapshot("FreeBuds", BatteryReadStatus.NotSupported, null, DeviceTransportKind.BluetoothClassic),
        });

        Assert.True(viewModel.HasDevices);
        Assert.False(viewModel.AreAllUnreachable);
        Assert.True(viewModel.ShowDeviceList);
    }

    [Fact]
    public void HiddenUnsupportedDevices_DoNotBlockUnreachableState()
    {
        using var viewModel = new MainViewModel();
        viewModel.ApplySettings(new AppSettings { ShowUnsupportedDevices = false });

        viewModel.ApplySnapshots(new[]
        {
            Snapshot("FreeBuds", BatteryReadStatus.NotSupported, null, DeviceTransportKind.BluetoothClassic),
            Snapshot("LINGBAO K98", BatteryReadStatus.Unavailable, null, DeviceTransportKind.Hid24G, Hid24GProtocol.LingbaoK98),
        });

        Assert.True(viewModel.AreAllUnreachable);
        Assert.False(viewModel.ShowDeviceList);
    }

    [Fact]
    public void UnreachableDeviceRefreshing_KeepsUnreachableState()
    {
        using var viewModel = new MainViewModel();
        var device = Snapshot("LINGBAO K98", BatteryReadStatus.Unavailable, null, DeviceTransportKind.Hid24G, Hid24GProtocol.LingbaoK98);
        viewModel.ApplySnapshots(new[] { device });
        Assert.True(viewModel.AreAllUnreachable);

        viewModel.ApplySnapshots(new[] { device with { Status = BatteryReadStatus.Reading } });

        Assert.True(viewModel.AreAllUnreachable);
        Assert.False(viewModel.ShowDeviceList);
    }

    [Fact]
    public void ReadableDeviceRefreshing_KeepsListVisible()
    {
        using var viewModel = new MainViewModel();
        var device = Snapshot("BLE Mouse", BatteryReadStatus.Ok, 85);
        viewModel.ApplySnapshots(new[] { device });

        viewModel.ApplySnapshots(new[] { device with { Status = BatteryReadStatus.Reading } });

        Assert.False(viewModel.AreAllUnreachable);
        Assert.True(viewModel.ShowDeviceList);
    }

    [Fact]
    public void RecoveredDevice_ReturnsToList()
    {
        using var viewModel = new MainViewModel();
        var device = Snapshot("LINGBAO K98", BatteryReadStatus.Unavailable, null, DeviceTransportKind.Hid24G, Hid24GProtocol.LingbaoK98);
        viewModel.ApplySnapshots(new[] { device });
        Assert.True(viewModel.AreAllUnreachable);

        viewModel.ApplySnapshots(new[] { device with { Status = BatteryReadStatus.Ok, BatteryPercent = 62 } });

        Assert.False(viewModel.AreAllUnreachable);
        Assert.True(viewModel.ShowDeviceList);
    }

    [Fact]
    public void ServiceError_ShowsScanFailureInfoBar()
    {
        using var viewModel = new MainViewModel();

        viewModel.ApplyServiceStatus(DeviceServiceStatus.Create(
            DeviceServiceState.Error,
            "本轮扫描失败，将在下一轮自动重试"));

        Assert.True(viewModel.IsScanFailed);
        Assert.Equal("本轮扫描失败，将在下一轮自动重试", viewModel.ScanFailureMessage);
        Assert.Equal(StatusSeverity.Caution, viewModel.StatusSeverity);
    }

    [Fact]
    public void NextSuccessfulStatus_ClearsScanFailure()
    {
        using var viewModel = new MainViewModel();
        viewModel.ApplyServiceStatus(DeviceServiceStatus.Create(
            DeviceServiceState.Error,
            "本轮扫描失败，将在下一轮自动重试"));

        viewModel.ApplyServiceStatus(DeviceServiceStatus.Create(
            DeviceServiceState.Ready,
            "蓝牙可用"));

        Assert.False(viewModel.IsScanFailed);
        Assert.Equal("蓝牙可用", viewModel.GlobalStatus);
        Assert.Equal(StatusSeverity.Normal, viewModel.StatusSeverity);
    }

    [Fact]
    public void BluetoothOff_IsCautionButNotScanFailure()
    {
        using var viewModel = new MainViewModel();

        viewModel.ApplyServiceStatus(DeviceServiceStatus.Create(
            DeviceServiceState.BluetoothOff,
            "蓝牙未开启"));

        Assert.False(viewModel.IsScanFailed);
        Assert.Equal(StatusSeverity.Caution, viewModel.StatusSeverity);
    }

    [Fact]
    public void NoDevices_IsEmptyNotUnreachable()
    {
        using var viewModel = new MainViewModel();

        viewModel.ApplySnapshots(Array.Empty<DeviceBatterySnapshot>());

        Assert.False(viewModel.HasDevices);
        Assert.False(viewModel.AreAllUnreachable);
        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.ShowDeviceList);
    }

    private static DeviceBatterySnapshot Snapshot(
        string name,
        BatteryReadStatus status,
        int? percent,
        DeviceTransportKind transport = DeviceTransportKind.BluetoothLe,
        Hid24GProtocol protocol = Hid24GProtocol.Unknown)
    {
        return new DeviceBatterySnapshot(
            Id: $"{transport}:{name}",
            Address: transport == DeviceTransportKind.Hid24G ? null : 0xA1B2C3D4E5F6UL,
            Name: name,
            Transport: transport,
            BatteryPercent: percent,
            Status: status,
            LastSuccessAt: percent.HasValue ? DateTimeOffset.Now.AddMinutes(-5) : null,
            LastAttemptAt: DateTimeOffset.Now,
            IsConnected: transport == DeviceTransportKind.Hid24G,
            SupportsNotifications: false,
            ChargingText: null)
        {
            Protocol = protocol,
        };
    }
}