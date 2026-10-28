using BlueMeter.Services;
using Xunit;

namespace BlueMeter.Tests;

public sealed class SystemBatteryPropertyReaderTests
{
    [Fact]
    public void BatteryLife_InRange_IsReturned()
    {
        var properties = new Dictionary<string, object>
        {
            [SystemBatteryPropertyReader.BatteryLifeKey] = (byte)55,
        };

        var (percent, charging) = SystemBatteryPropertyReader.Read(properties);

        Assert.Equal(55, percent);
        Assert.False(charging);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)101)]
    [InlineData((byte)200)]
    public void BatteryLife_UnknownCodes_AreIgnored(byte value)
    {
        var properties = new Dictionary<string, object>
        {
            [SystemBatteryPropertyReader.BatteryLifeKey] = value,
        };

        var (percent, charging) = SystemBatteryPropertyReader.Read(properties);

        Assert.Null(percent);
        Assert.False(charging);
    }

    [Fact]
    public void MissingProperties_ReturnUnknown()
    {
        var (percent, charging) = SystemBatteryPropertyReader.Read(
            new Dictionary<string, object>());

        Assert.Null(percent);
        Assert.False(charging);
    }

    [Theory]
    [InlineData(145, 45)]
    [InlineData(100, 0)]
    [InlineData(200, 100)]
    public void BatteryPlusCharging_Over100_ReportsCharging(int raw, int expectedPercent)
    {
        var properties = new Dictionary<string, object>
        {
            [SystemBatteryPropertyReader.BatteryPlusChargingKey] = (byte)raw,
        };

        var (percent, charging) = SystemBatteryPropertyReader.Read(properties);

        Assert.Equal(expectedPercent, percent);
        Assert.True(charging);
    }

    [Fact]
    public void BatteryPlusCharging_PlainPercent_IsNotCharging()
    {
        var properties = new Dictionary<string, object>
        {
            [SystemBatteryPropertyReader.BatteryPlusChargingKey] = (byte)33,
        };

        var (percent, charging) = SystemBatteryPropertyReader.Read(properties);

        Assert.Equal(33, percent);
        Assert.False(charging);
    }

    [Fact]
    public void BatteryPlusCharging_Zero_FallsBackToBatteryLife()
    {
        var properties = new Dictionary<string, object>
        {
            [SystemBatteryPropertyReader.BatteryPlusChargingKey] = (byte)0,
            [SystemBatteryPropertyReader.BatteryLifeKey] = (byte)72,
        };

        var (percent, charging) = SystemBatteryPropertyReader.Read(properties);

        Assert.Equal(72, percent);
        Assert.False(charging);
    }

    [Fact]
    public void StringValue_IsParsed()
    {
        var properties = new Dictionary<string, object>
        {
            [SystemBatteryPropertyReader.BatteryLifeKey] = "80",
        };

        var (percent, _) = SystemBatteryPropertyReader.Read(properties);

        Assert.Equal(80, percent);
    }
}
