using BlueMeter.Models;
using Xunit;

namespace BlueMeter.Tests;

public sealed class RefreshIntervalConverterTests
{
    [Theory]
    [InlineData(45, RefreshIntervalUnit.Seconds, 45)]
    [InlineData(2, RefreshIntervalUnit.Minutes, 120)]
    [InlineData(3, RefreshIntervalUnit.Hours, 10800)]
    public void ToSeconds_AppliesUnitMultiplier(int value, RefreshIntervalUnit unit, int expected)
    {
        Assert.Equal(expected, RefreshIntervalConverter.ToSeconds(value, unit));
    }

    [Theory]
    [InlineData(45, 45, RefreshIntervalUnit.Seconds)]
    [InlineData(120, 2, RefreshIntervalUnit.Minutes)]
    [InlineData(90, 90, RefreshIntervalUnit.Seconds)]
    [InlineData(5400, 90, RefreshIntervalUnit.Minutes)]
    [InlineData(3600, 1, RefreshIntervalUnit.Hours)]
    public void FromSeconds_PrefersLargestWholeUnit(int seconds, int expectedValue, RefreshIntervalUnit expectedUnit)
    {
        var (value, unit) = RefreshIntervalConverter.FromSeconds(seconds);

        Assert.Equal(expectedValue, value);
        Assert.Equal(expectedUnit, unit);
    }

    [Fact]
    public void RoundTrip_KeepsSecondsIntact()
    {
        foreach (var seconds in new[] { 10, 45, 60, 120, 3600, 7200, 86400 })
        {
            var (value, unit) = RefreshIntervalConverter.FromSeconds(seconds);

            Assert.Equal(seconds, RefreshIntervalConverter.ToSeconds(value, unit));
        }
    }

    [Fact]
    public void UnitText_IsChinese()
    {
        Assert.Equal("秒", RefreshIntervalConverter.UnitText(RefreshIntervalUnit.Seconds));
        Assert.Equal("分钟", RefreshIntervalConverter.UnitText(RefreshIntervalUnit.Minutes));
        Assert.Equal("小时", RefreshIntervalConverter.UnitText(RefreshIntervalUnit.Hours));
    }

    [Fact]
    public void Normalize_ClampsIntervalToSupportedRange()
    {
        var tooSmall = new AppSettings { RefreshIntervalSeconds = 1 };
        tooSmall.Normalize();
        Assert.Equal(AppSettings.MinRefreshIntervalSeconds, tooSmall.RefreshIntervalSeconds);

        var tooLarge = new AppSettings { RefreshIntervalSeconds = 100_000 };
        tooLarge.Normalize();
        Assert.Equal(AppSettings.MaxRefreshIntervalSeconds, tooLarge.RefreshIntervalSeconds);
    }

    [Fact]
    public void Normalize_KeepsUnusualButValidInterval()
    {
        var settings = new AppSettings { RefreshIntervalSeconds = 47 };
        settings.Normalize();

        Assert.Equal(47, settings.RefreshIntervalSeconds);
    }
}
