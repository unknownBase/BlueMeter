using BlueMeter.Models;
using BlueMeter.ViewModels;
using Xunit;

namespace BlueMeter.Tests;

public sealed class DeviceItemViewModelTests
{
    private const string WakeHint = "按键唤醒后刷新";

    [Fact]
    public void K98Unreachable_ShowsWakeHint()
    {
        var item = Create(
            Hid24GProtocol.LingbaoK98,
            BatteryReadStatus.Stale,
            percent: 62,
            lastSuccessAt: DateTimeOffset.Now.AddMinutes(-5));

        Assert.Contains("暂时不可达", item.MetaText);
        Assert.Contains(WakeHint, item.MetaText);
    }

    [Fact]
    public void K98WithNoReading_ShowsWakeHint()
    {
        var item = Create(
            Hid24GProtocol.LingbaoK98,
            BatteryReadStatus.Unavailable,
            percent: null,
            lastSuccessAt: null);

        Assert.Contains($"暂时不可达，{WakeHint}", item.MetaText);
    }

    [Fact]
    public void K98Readable_HasNoWakeHint()
    {
        var item = Create(
            Hid24GProtocol.LingbaoK98,
            BatteryReadStatus.Ok,
            percent: 62,
            lastSuccessAt: DateTimeOffset.Now);

        Assert.DoesNotContain(WakeHint, item.MetaText);
    }

    [Fact]
    public void Other24GDevice_HasNoWakeHint()
    {
        var item = Create(
            Hid24GProtocol.Rapoo,
            BatteryReadStatus.Stale,
            percent: 62,
            lastSuccessAt: DateTimeOffset.Now.AddMinutes(-5));

        Assert.Contains("暂时不可达", item.MetaText);
        Assert.DoesNotContain(WakeHint, item.MetaText);
    }

    private static DeviceItemViewModel Create(
        Hid24GProtocol protocol,
        BatteryReadStatus status,
        int? percent,
        DateTimeOffset? lastSuccessAt)
    {
        var snapshot = new DeviceBatterySnapshot(
            Id: "hid:receiver",
            Address: null,
            Name: "LINGBAO K98 2.4G",
            Transport: DeviceTransportKind.Hid24G,
            BatteryPercent: percent,
            Status: status,
            LastSuccessAt: lastSuccessAt,
            LastAttemptAt: DateTimeOffset.Now,
            IsConnected: true,
            SupportsNotifications: false,
            ChargingText: null)
        {
            VendorName = "LINGBAO",
            Protocol = protocol,
        };

        return new DeviceItemViewModel(snapshot, lowBatteryThreshold: 20);
    }
}
