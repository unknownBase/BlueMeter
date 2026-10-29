using BlueMeter.Models;
using BlueMeter.Services;
using Xunit;

namespace BlueMeter.Tests;

public sealed class TrayMenuBuilderTests
{
    [Fact]
    public void NoReadableBattery_ReturnsEmpty()
    {
        var entries = TrayMenuBuilder.Build(
            new[]
            {
                Snapshot("耳机", null, BatteryReadStatus.NotSupported),
                Snapshot("手柄", null, BatteryReadStatus.Unavailable),
            },
            lowBatteryThreshold: 20);

        Assert.Empty(entries);
    }

    [Fact]
    public void SingleDevice_ShowsNameAndPercent()
    {
        var entries = TrayMenuBuilder.Build(
            new[] { Snapshot("鼠标", 80, BatteryReadStatus.Ok) },
            lowBatteryThreshold: 20);

        Assert.Equal(new[] { "鼠标 · 80%" }, entries);
    }

    [Fact]
    public void LowBattery_IsMarkedInText()
    {
        var entries = TrayMenuBuilder.Build(
            new[] { Snapshot("鼠标", 15, BatteryReadStatus.Ok) },
            lowBatteryThreshold: 20);

        Assert.Equal("鼠标 · 15% · 电量低", Assert.Single(entries));
    }

    [Fact]
    public void StaleValue_IsMarkedAsOldValue()
    {
        var entries = TrayMenuBuilder.Build(
            new[] { Snapshot("键盘", 53, BatteryReadStatus.Stale) },
            lowBatteryThreshold: 20);

        Assert.Equal("键盘 · 53% · 旧值", Assert.Single(entries));
    }

    [Fact]
    public void MoreThanThreeDevices_KeepsLowestThree()
    {
        var entries = TrayMenuBuilder.Build(
            new[]
            {
                Snapshot("鼠标", 90, BatteryReadStatus.Ok),
                Snapshot("键盘", 12, BatteryReadStatus.Ok),
                Snapshot("耳机", 55, BatteryReadStatus.Ok),
                Snapshot("手柄", 30, BatteryReadStatus.Ok),
            },
            lowBatteryThreshold: 20);

        Assert.Equal(3, entries.Count);
        Assert.Equal("键盘 · 12% · 电量低", entries[0]);
        Assert.Equal("手柄 · 30%", entries[1]);
        Assert.Equal("耳机 · 55%", entries[2]);
    }

    [Fact]
    public void LongDeviceName_IsTruncated()
    {
        var entries = TrayMenuBuilder.Build(
            new[] { Snapshot(new string('长', 60), 42, BatteryReadStatus.Ok) },
            lowBatteryThreshold: 20);

        var text = Assert.Single(entries);
        Assert.Contains("…", text, StringComparison.Ordinal);
        Assert.True(text.Length < 60, $"length={text.Length}");
    }

    [Fact]
    public void SectionTitle_ReflectsRefreshState()
    {
        Assert.Equal("设备电量", TrayMenuBuilder.SectionTitle(refreshing: false));
        Assert.Equal("设备电量 · 正在刷新…", TrayMenuBuilder.SectionTitle(refreshing: true));
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
