namespace BlueMeter.Services;

/// <summary>
/// 托盘上可由展示逻辑驱动的入口。生产实现是 <see cref="TrayIconService"/>，
/// 单测里用记录调用的假实现即可验证“显示什么”，无需真的创建托盘图标。
/// </summary>
public interface ITraySurface
{
    void SetToolTip(string? text);

    void SetRefreshBusy(bool busy);

    void SetDeviceBatteryMenu(bool enabled, IReadOnlyList<string> headers);
}
