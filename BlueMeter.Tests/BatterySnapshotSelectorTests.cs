using BlueMeter.Models;
using BlueMeter.Services;
using Xunit;

namespace BlueMeter.Tests;

public sealed class BatterySnapshotSelectorTests
{
    [Fact]
    public void SkipsDevicesWithoutBattery()
    {
        var selected = BatterySnapshotSelector.ReadableLowestFirst(new[]
        {
            Snapshot("耳机", null, BatteryReadStatus.NotSupported),
            Snapshot("鼠标", 80, BatteryReadStatus.Ok),
        });

        Assert.Equal("鼠标", Assert.Single(selected).Name);
    }

    [Fact]
    public void OrdersByPercentThenName()
    {
        var selected = BatterySnapshotSelector.ReadableLowestFirst(new[]
        {
            Snapshot("B 键盘", 30, BatteryReadStatus.Ok),
            Snapshot("A 鼠标", 30, BatteryReadStatus.Ok),
            Snapshot("耳机", 12, BatteryReadStatus.Ok),
        });

        Assert.Equal(
            new[] { "耳机", "A 鼠标", "B 键盘" },
            selected.Select(snapshot => snapshot.Name));
    }

    [Fact]
    public void Take_LimitsResultToLowestDevices()
    {
        var selected = BatterySnapshotSelector.ReadableLowestFirst(
            new[]
            {
                Snapshot("鼠标", 90, BatteryReadStatus.Ok),
                Snapshot("键盘", 12, BatteryReadStatus.Ok),
                Snapshot("耳机", 55, BatteryReadStatus.Ok),
            },
            take: 2);

        Assert.Equal(new[] { "键盘", "耳机" }, selected.Select(snapshot => snapshot.Name));
    }

    [Fact]
    public void KeepsStaleValues()
    {
        var selected = BatterySnapshotSelector.ReadableLowestFirst(new[]
        {
            Snapshot("键盘", 53, BatteryReadStatus.Stale),
        });

        Assert.Equal(53, Assert.Single(selected).BatteryPercent);
    }

    internal static DeviceBatterySnapshot Snapshot(
        string name,
        int? percent,
        BatteryReadStatus status,
        DeviceTransportKind transport = DeviceTransportKind.BluetoothLe) =>
        new(
            Id: name,
            Address: null,
            Name: name,
            Transport: transport,
            BatteryPercent: percent,
            Status: status,
            LastSuccessAt: percent.HasValue ? DateTimeOffset.Now : null,
            LastAttemptAt: DateTimeOffset.Now,
            IsConnected: true,
            SupportsNotifications: false,
            ChargingText: null);
}
