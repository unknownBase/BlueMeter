using System.Globalization;
using System.Windows.Data;
using BlueMeter.Models;
using Wpf.Ui.Controls;

namespace BlueMeter.Converters;

/// <summary>把推断出的设备类别映射成 Fluent 图标；仅辅助识别，不能作为唯一信息。</summary>
public sealed class DeviceCategoryToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DeviceCategory category
            ? category switch
            {
                DeviceCategory.Mouse => SymbolRegular.Cursor24,
                DeviceCategory.Keyboard => SymbolRegular.Keyboard24,
                DeviceCategory.Headset => SymbolRegular.Headphones24,
                DeviceCategory.Gamepad => SymbolRegular.XboxController24,
                DeviceCategory.Phone => SymbolRegular.Phone24,
                _ => SymbolRegular.Bluetooth24,
            }
            : SymbolRegular.Bluetooth24;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
