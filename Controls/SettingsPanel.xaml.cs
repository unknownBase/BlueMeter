using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BlueMeter.ViewModels;

namespace BlueMeter.Controls;

/// <summary>
/// 设置面板视图：只负责把输入事件转给 <see cref="SettingsViewModel"/>，
/// 保存、回滚与注册表副作用都在 App。
/// </summary>
public partial class SettingsPanel : UserControl
{
    public SettingsPanel()
    {
        InitializeComponent();
    }

    private SettingsViewModel? Settings => (DataContext as MainViewModel)?.Settings;

    private void OnRefreshIntervalLostFocus(object sender, RoutedEventArgs e) =>
        Settings?.CommitRefreshInterval();

    private void OnRefreshIntervalKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        Settings?.CommitRefreshInterval();
        e.Handled = true;
    }
}
