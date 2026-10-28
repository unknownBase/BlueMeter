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

    public static DeviceCategory Classify(string? name)
    {
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
