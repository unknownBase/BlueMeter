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
            lastSuccessAt: DateTimeOffset.Now.AddMinutes(-5),
            consecutiveFailures: 2);

        Assert.Contains("暂时不可达", item.MetaText);
        Assert.Contains(WakeHint, item.MetaText);
    }

    [Fact]
    public void K98WithNoReading_ShowsWakeHintAfterTwoFailures()
    {
        var item = Create(
            Hid24GProtocol.LingbaoK98,
            BatteryReadStatus.Unavailable,
            percent: null,
            lastSuccessAt: null,
            consecutiveFailures: 2);

        Assert.Contains($"暂时不可达，{WakeHint}", item.MetaText);
    }

    [Fact]
    public void K98FirstFailure_HidesWakeHint()
    {
        // 进程刚启动时接收器还在被系统枚举，首轮失败不提示按键唤醒。
        var item = Create(
            Hid24GProtocol.LingbaoK98,
            BatteryReadStatus.Unavailable,
            percent: null,
            lastSuccessAt: null,
            consecutiveFailures: 1);

        Assert.Contains("暂时不可达", item.MetaText);
        Assert.DoesNotContain(WakeHint, item.MetaText);
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
            lastSuccessAt: DateTimeOffset.Now.AddMinutes(-5),
            consecutiveFailures: 3);

        Assert.Contains("暂时不可达", item.MetaText);
        Assert.DoesNotContain(WakeHint, item.MetaText);
    }

    private static DeviceItemViewModel Create(
        Hid24GProtocol protocol,
        BatteryReadStatus status,
        int? percent,
        DateTimeOffset? lastSuccessAt,
        int consecutiveFailures = 0)
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
            ConsecutiveFailures = consecutiveFailures,
        };

        return new DeviceItemViewModel(snapshot, lowBatteryThreshold: 20);
    }
}
