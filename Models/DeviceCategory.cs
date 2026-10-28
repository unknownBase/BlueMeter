namespace BlueMeter.Models;

/// <summary>
/// 设备类别，仅用于选择列表行图标；无法判断时用 <see cref="Other"/>。
/// 类别来自设备名关键词，不作为业务判断依据。
/// </summary>
public enum DeviceCategory
{
    Other,
    Mouse,
    Keyboard,
    Headset,
    Gamepad,
    Phone,
}

/// <summary>按设备名关键词猜测类别；2.4G 接收器名称里带产品型号，同样适用。</summary>
public static class DeviceCategoryClassifier
{
    private static readonly string[] MouseKeywords = ["mouse", "鼠标"];
    private static readonly string[] KeyboardKeywords = ["keyboard", "键盘", "k98"];
    private static readonly string[] HeadsetKeywords =
        ["headset", "headphone", "earbud", "buds", "耳机", "airpods", "speaker", "音箱"];
    private static readonly string[] GamepadKeywords = ["gamepad", "controller", "手柄", "xbox"];
    private static readonly string[] PhoneKeywords = ["phone", "手机", "iphone", "tablet", "平板"];

    /// <summary>
    /// 先按 2.4G 厂商协议推断，再退回名称关键词。
    /// 2.4G 接收器的产品串只报厂商通用名（例如 “Rapoo Gaming Device”），
    /// 名称里没有鼠标/键盘字样，只能靠协议区分。
    /// </summary>
    public static DeviceCategory Classify(string? name, Hid24GProtocol protocol = Hid24GProtocol.Unknown)
    {
        switch (protocol)
        {
            case Hid24GProtocol.Rapoo:
                return DeviceCategory.Mouse;
            case Hid24GProtocol.LingbaoK98:
                return DeviceCategory.Keyboard;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return DeviceCategory.Other;
        }

        if (Matches(name, MouseKeywords))
        {
            return DeviceCategory.Mouse;
        }

        if (Matches(name, KeyboardKeywords))
        {
            return DeviceCategory.Keyboard;
        }

        if (Matches(name, HeadsetKeywords))
        {
            return DeviceCategory.Headset;
        }

        if (Matches(name, GamepadKeywords))
        {
            return DeviceCategory.Gamepad;
        }

        return Matches(name, PhoneKeywords) ? DeviceCategory.Phone : DeviceCategory.Other;
    }

    private static bool Matches(string name, string[] keywords) =>
        keywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
}
