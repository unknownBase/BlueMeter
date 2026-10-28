using BlueMeter.Models;
using BlueMeter.Services;
using Xunit;

namespace BlueMeter.Tests;

public sealed class LowBatteryMessageTests
{
    [Fact]
    public void SingleDevice_UsesSimpleSentence()
    {
        var message = LowBatteryNotifier.BuildMessage(new[]
        {
            new LowBatteryNotice("1", "鼠标", 15),
        });

        Assert.Equal("鼠标电量低：15%", message);
    }

    [Fact]
    public void MultipleDevices_ReportCountAndLowest()
    {
        var message = LowBatteryNotifier.BuildMessage(new[]
        {
            new LowBatteryNotice("1", "鼠标", 18),
            new LowBatteryNotice("2", "键盘", 12),
        });

        Assert.Equal("2 个设备电量低，最低：键盘 12%", message);
    }

    [Fact]
    public void EmptyBatch_ReturnsEmptyMessage()
    {
        Assert.Equal(string.Empty, LowBatteryNotifier.BuildMessage(Array.Empty<LowBatteryNotice>()));
    }

    [Fact]
    public void LongDeviceName_IsShortened()
    {
        var message = LowBatteryNotifier.BuildMessage(new[]
        {
            new LowBatteryNotice("1", new string('长', 80), 9),
        });

        Assert.Contains("…", message);
        Assert.True(message.Length < 60, $"length={message.Length}");
    }

    [Fact]
    public void Notify_WithNotificationsDisabled_DoesNotSend()
    {
        var sent = 0;
        using var notifier = new LowBatteryNotifier(
            notificationsEnabled: () => false,
            showNotification: _ => Interlocked.Increment(ref sent));

        notifier.Notify(new[] { new LowBatteryNotice("1", "鼠标", 10) });

        // 合并窗口结束后仍应被开关拦下。
        Thread.Sleep(TimeSpan.FromSeconds(2));
        Assert.Equal(0, Volatile.Read(ref sent));
    }

    [Fact]
    public void Notify_WithNotificationsEnabled_SendsSingleAggregatedMessage()
    {
        var messages = new List<string>();
        using var notifier = new LowBatteryNotifier(
            notificationsEnabled: () => true,
            showNotification: message =>
            {
                lock (messages)
                {
                    messages.Add(message);
                }
            });

        notifier.Notify(new[] { new LowBatteryNotice("1", "鼠标", 10) });
        notifier.Notify(new[] { new LowBatteryNotice("2", "键盘", 8) });

        Thread.Sleep(TimeSpan.FromSeconds(3));

        lock (messages)
        {
            Assert.Single(messages);
            Assert.Contains("2 个设备电量低", messages[0]);
            Assert.Contains("键盘 8%", messages[0]);
        }
    }
}
