using BlueMeter.Models;
using BlueMeter.ViewModels;
using Xunit;

namespace BlueMeter.Tests;

public sealed class DeviceListProjectorTests
{
    [Fact]
    public void ShowUnsupported_KeepsEveryDevice()
    {
        var projection = DeviceListProjector.Create(
            new[]
            {
                Snapshot("鼠标", 70, BatteryReadStatus.Ok),
                Snapshot("无电池设备", null, BatteryReadStatus.NotSupported),
            },
            lowBatteryThreshold: 20,
            showUnsupportedDevices: true);

        Assert.Equal(2, projection.Items.Count);
        Assert.Equal(0, projection.HiddenUnsupportedCount);
    }

    [Fact]
    public void HideUnsupported_DropsNotSupportedOnly()
    {
        var projection = DeviceListProjector.Create(
            new[]
            {
                Snapshot("鼠标", 70, BatteryReadStatus.Ok),
                Snapshot("无电池设备", null, BatteryReadStatus.NotSupported),
                Snapshot("离线键盘", null, BatteryReadStatus.Unavailable),
            },
            lowBatteryThreshold: 20,
            showUnsupportedDevices: false);

        Assert.Equal(2, projection.Items.Count);
        Assert.Equal(1, projection.HiddenUnsupportedCount);
        Assert.DoesNotContain(projection.Items, item => item.Name == "无电池设备");
        Assert.Contains(projection.Items, item => item.Name == "离线键盘");
    }

    [Fact]
    public void LowBatteryDevice_ComesFirst()
    {
        var projection = DeviceListProjector.Create(
            new[]
            {
                Snapshot("充足设备", 90, BatteryReadStatus.Ok),
                Snapshot("低电量设备", 10, BatteryReadStatus.Ok),
            },
            lowBatteryThreshold: 20,
            showUnsupportedDevices: true);

        Assert.Equal("低电量设备", projection.Items[0].Name);
    }

    [Fact]
    public void ThresholdChange_ReordersUsingSameSnapshots()
    {
        var snapshots = new[]
        {
            Snapshot("Zeta", 40, BatteryReadStatus.Ok),
            Snapshot("Alpha", 60, BatteryReadStatus.Ok),
        };

        // 阈值 50：Zeta 落进低电量组排最前；阈值 5：两台都落在正常组，同组按电量升序。
        var strict = DeviceListProjector.Create(snapshots, lowBatteryThreshold: 50, showUnsupportedDevices: true);
        var lenient = DeviceListProjector.Create(snapshots, lowBatteryThreshold: 5, showUnsupportedDevices: true);

        Assert.Equal("Zeta", strict.Items[0].Name);
        Assert.True(strict.Items[0].IsLowBattery);
        Assert.Equal("Zeta", lenient.Items[0].Name);
        Assert.False(lenient.Items[0].IsLowBattery);
    }

    [Fact]
    public void UnreadDevice_IsRankedAfterKnownValues()
    {
        var projection = DeviceListProjector.Create(
            new[]
            {
                Snapshot("未知设备", null, BatteryReadStatus.Unknown),
                Snapshot("离线设备", null, BatteryReadStatus.Stale),
                Snapshot("正常设备", 55, BatteryReadStatus.Ok),
            },
            lowBatteryThreshold: 20,
            showUnsupportedDevices: true);

        Assert.Equal(
            new[] { "正常设备", "离线设备", "未知设备" },
            projection.Items.Select(item => item.Name).ToArray());
    }

    private static DeviceBatterySnapshot Snapshot(
        string name,
        int? percent,
        BatteryReadStatus status) =>
        new(
            Id: name,
            Address: null,
            Name: name,
            Transport: DeviceTransportKind.BluetoothLe,
            BatteryPercent: percent,
            Status: status,
            LastSuccessAt: percent.HasValue ? DateTimeOffset.Now : null,
            LastAttemptAt: DateTimeOffset.Now,
            IsConnected: true,
            SupportsNotifications: false,
            ChargingText: null);
}
