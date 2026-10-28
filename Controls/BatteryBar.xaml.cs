using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using BlueMeter.Models;

namespace BlueMeter.Controls;

/// <summary>
/// 电量条：圆角轨道 + 按百分比伸缩的填充。填充颜色由 <see cref="Level"/> 决定，
/// 数值变化时 150ms 缓动；系统关闭动画或开启高对比度时立即切换。
/// </summary>
public partial class BatteryBar : UserControl
{
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(150);

    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent),
        typeof(double?),
        typeof(BatteryBar),
        new PropertyMetadata(null, OnPercentChanged));

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level),
        typeof(BatteryLevel),
        typeof(BatteryBar),
        new PropertyMetadata(BatteryLevel.Unknown));

    private static readonly DependencyProperty AnimatedPercentProperty =
        DependencyProperty.Register(
            nameof(AnimatedPercent),
            typeof(double),
            typeof(BatteryBar),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.None, OnAnimatedPercentChanged));

    private bool _initialized;

    public BatteryBar()
    {
        InitializeComponent();
    }

    /// <summary>0–100 的电量；null 表示没有读数，模板会隐藏整个控件。</summary>
    public double? Percent
    {
        get => (double?)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public BatteryLevel Level
    {
        get => (BatteryLevel)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public double AnimatedPercent => (double)GetValue(AnimatedPercentProperty);

    private static void OnPercentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BatteryBar bar)
        {
            bar.ApplyPercent((double?)e.NewValue);
        }
    }

    private static void OnAnimatedPercentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BatteryBar bar)
        {
            bar.UpdateFillWidth((double)e.NewValue);
        }
    }

    private void ApplyPercent(double? percent)
    {
        var target = Math.Clamp(percent ?? 0, 0, 100);

        if (!_initialized || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
        {
            _initialized = true;
            BeginAnimation(AnimatedPercentProperty, null);
            SetValue(AnimatedPercentProperty, target);
            return;
        }

        var animation = new DoubleAnimation(target, AnimationDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };

        BeginAnimation(AnimatedPercentProperty, animation);
    }

    private void UpdateFillWidth(double percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        FillColumn.Width = new GridLength(clamped, GridUnitType.Star);
        RemainderColumn.Width = new GridLength(100 - clamped, GridUnitType.Star);

        // 0% 时隐藏填充，避免留一条圆角残影。
        Fill.Visibility = clamped <= 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
