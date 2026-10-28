using Microsoft.Win32;

namespace BlueMeter.Services;

/// <summary>
/// 通过当前用户的 <c>Run</c> 键管理“开机时自动启动”。
/// 写 HKCU 不需要管理员权限；失败只记日志并返回 <c>false</c>，由调用方回滚设置值。
/// </summary>
public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BlueMeter";

    /// <summary>把可执行文件路径包装成 Run 键要求的带引号命令行。</summary>
    public static string QuoteCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return $"\"{executablePath.Trim().Trim('"')}\"";
    }

    /// <summary>当前进程的可执行文件路径；单文件/框架依赖两种发布方式下都成立。</summary>
    public static string? BuildCommand()
    {
        var path = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(path) ? null : QuoteCommand(path);
    }

    /// <summary>Run 键里是否存在 BlueMeter 项（不校验路径是否过期）。</summary>
    public bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string command && command.Length > 0;
        }
        catch (Exception ex)
        {
            AppLog.Warn("读取开机启动项失败", "Startup", ex);
            return false;
        }
    }

    /// <summary>
    /// 启动时对齐设置与注册表：开启但没有有效命令（或路径已变）时补写，关闭但仍有残留时删除。
    /// </summary>
    public void Reconcile(bool enabled)
    {
        var command = BuildCommand();
        var existing = ReadCommand();

        if (!enabled)
        {
            if (existing is null)
            {
                return;
            }

            if (TrySetEnabled(false))
            {
                AppLog.Info("设置已关闭开机启动，清除残留启动项", "Startup");
            }

            return;
        }

        if (command is null)
        {
            AppLog.Warn("无法确定可执行文件路径，跳过开机启动注册", "Startup");
            return;
        }

        if (string.Equals(existing, command, StringComparison.OrdinalIgnoreCase))
        {
            AppLog.Debug("开机启动项已是最新", "Startup");
            return;
        }

        if (TrySetEnabled(true))
        {
            AppLog.Info($"已写入开机启动项：{command}", "Startup");
        }
    }

    /// <summary>写入或删除开机启动项；返回 <c>false</c> 表示注册表操作失败。</summary>
    public bool TrySetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                AppLog.Warn("无法打开开机启动注册表键", "Startup");
                return false;
            }

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            var command = BuildCommand();
            if (command is null)
            {
                AppLog.Warn("无法确定可执行文件路径，跳过开机启动注册", "Startup");
                return false;
            }

            key.SetValue(ValueName, command, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("写入开机启动项失败", "Startup", ex);
            return false;
        }
    }

    private static string? ReadCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) as string;
        }
        catch (Exception ex)
        {
            AppLog.Warn("读取开机启动命令失败", "Startup", ex);
            return null;
        }
    }
}
