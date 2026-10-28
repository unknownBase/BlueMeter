using BlueMeter.Models;
using Xunit;

namespace BlueMeter.Tests;

public sealed class DeviceCategoryClassifierTests
{
    [Fact]
    public void RapooReceiver_UsesProtocolBecauseProductNameIsGeneric()
    {
        // Rapoo 2.4G 接收器的产品串固定为 “Rapoo Gaming Device”，名称里没有鼠标字样。
        var category = DeviceCategoryClassifier.Classify("Rapoo Gaming Device", Hid24GProtocol.Rapoo);

        Assert.Equal(DeviceCategory.Mouse, category);
    }

    [Fact]
    public void K98Receiver_UsesProtocol()
    {
        var category = DeviceCategoryClassifier.Classify("LINGBAO K98 2.4G", Hid24GProtocol.LingbaoK98);

        Assert.Equal(DeviceCategory.Keyboard, category);
    }

    [Theory]
    [InlineData("HUAWEI FreeBuds 7i", DeviceCategory.Headset)]
    [InlineData("Xbox Wireless Controller", DeviceCategory.Gamepad)]
    [InlineData("iPhone 15", DeviceCategory.Phone)]
    [InlineData("MX Master 3S Mouse", DeviceCategory.Mouse)]
    [InlineData("机械键盘 K87", DeviceCategory.Keyboard)]
    public void NameKeywords_StillClassifyWithoutProtocol(string name, DeviceCategory expected)
    {
        Assert.Equal(expected, DeviceCategoryClassifier.Classify(name));
    }

    [Fact]
    public void UnknownName_ReturnsOther()
    {
        Assert.Equal(DeviceCategory.Other, DeviceCategoryClassifier.Classify("Rapoo Gaming Device"));
        Assert.Equal(DeviceCategory.Other, DeviceCategoryClassifier.Classify(null));
    }
}
