using BlueMeter.Models;
using BlueMeter.Services;
using Xunit;

namespace BlueMeter.Tests;

public sealed class TraySummaryBuilderTests
{
    [Fact]
    public void NoDevices_ReturnsEmptyHint()
    {
        var text = TraySummaryBuilder.Build(Array.Empty<DeviceBatterySnapshot>());

        Assert.Equal("BlueMeter · 未发现蓝牙或 2.4G 设备", text);
    }

    [Fact]
    public void Scanning_IsDistinctFromEmpty()
    {
        Assert.NotEqual(TraySummaryBuilder.Scanning(), TraySummaryBuilder.Build(Array.Empty<DeviceBatterySnapshot>()));
        Assert.Contains("正在扫描", TraySummaryBuilder.Scanning());
    }

    [Fact]
    public void SingleDevice_ShowsNameAndPercent()
    {
        var text = TraySummaryBuilder.Build(new[]
        {
            Snapshot("鼠标", 15, BatteryReadStatus.Ok),
        });

        Assert.Equal("BlueMeter · 鼠标 15%", text);
    }

    [Fact]
    public void SingleStaleDevice_IsMarkedUnreachable()
    {
        var text = TraySummaryBuilder.Build(new[]
        {
            Snapshot("鼠标", 15, BatteryReadStatus.Stale),
        });

        Assert.Contains("鼠标 15%", text);
        Assert.Contains("暂时不可达", text);
    }

    [Fact]
    public void MultipleDevices_ShowLowestPercent()
    {
        var text = TraySummaryBuilder.Build(new[]
        {
            Snapshot("鼠标", 80, BatteryReadStatus.Ok),
            Snapshot("键盘", 12, BatteryReadStatus.Ok),
            Snapshot("耳机", 55, BatteryReadStatus.Ok),
        });

        Assert.Equal("BlueMeter · 3 台设备，最低 12%", text);
    }

    [Fact]
    public void MultipleDevicesWithStaleValue_IsMarked()
    {
        var text = TraySummaryBuilder.Build(new[]
        {
            Snapshot("鼠标", 80, BatteryReadStatus.Ok),
            Snapshot("键盘", 12, BatteryReadStatus.Stale),
        });

        Assert.Contains("最低 12%", text);
        Assert.Contains("含旧值", text);
    }

    [Fact]
    public void DevicesWithoutBattery_ReportNoReading()
    {
        var text = TraySummaryBuilder.Build(new[]
        {
            Snapshot("耳机", null, BatteryReadStatus.NotSupported),
            Snapshot("手柄", null, BatteryReadStatus.Unavailable),
        });

        Assert.Contains("2 台设备", text);
        Assert.Contains("暂无可读电量", text);
    }

    [Fact]
    public void LongDeviceName_KeepsToolTipWithinLimit()
    {
        var text = TraySummaryBuilder.Build(new[]
        {
            Snapshot(new string('长', 200), 42, BatteryReadStatus.Ok),
        });

        Assert.True(text.Length <= TraySummaryBuilder.MaxLength, $"length={text.Length}");
    }

    [Fact]
    public void StatusHint_IsPrefixedAndBounded()
    {
        Assert.Equal("BlueMeter · 未检测到蓝牙适配器", TraySummaryBuilder.StatusHint("未检测到蓝牙适配器"));
        Assert.True(TraySummaryBuilder.StatusHint(new string('错', 300)).Length <= TraySummaryBuilder.MaxLength);
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
