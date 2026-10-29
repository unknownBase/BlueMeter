using System.IO;
using System.Text;
using System.Text.Json;
using BlueMeter.Models;

namespace BlueMeter.Services;

/// <summary>
/// 负责 <c>%LOCALAPPDATA%\BlueMeter\settings.json</c> 的加载、校验与原子保存。
/// </summary>
public sealed class SettingsService
{
    private const string SettingsFileName = "settings.json";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _syncRoot = new();

    public SettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BlueMeter"))
    {
    }

    /// <summary>指定设置目录：实际文件为该目录下的 <c>settings.json</c>，便于测试或迁移。</summary>
    public SettingsService(string settingsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDirectory);

        SettingsDirectory = settingsDirectory;
        SettingsPath = Path.Combine(SettingsDirectory, SettingsFileName);
        Current = Load();
    }

    public string SettingsDirectory { get; }

    public string SettingsPath { get; }

    public AppSettings Current { get; private set; }

    public event EventHandler<AppSettings>? SettingsChanged;

    /// <summary>
    /// 修改设置、校验并立即保存。保存失败会记录日志，但不向调用方抛异常。
    /// </summary>
    public bool Update(Action<AppSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        AppSettings next;
        lock (_syncRoot)
        {
            next = Current.Clone();
            update(next);
            next.Normalize();

            if (!Save(next))
            {
                return false;
            }

            Current = next;
        }

        SettingsChanged?.Invoke(this, Current);
        return true;
    }

    public bool Save()
    {
        AppSettings snapshot;
        lock (_syncRoot)
        {
            snapshot = Current.Clone();
        }

        return Save(snapshot);
    }

    private AppSettings Load()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);

            if (!File.Exists(SettingsPath))
            {
                var defaults = new AppSettings();
                Save(defaults);
                AppLog.Info("未找到设置文件，已写入默认值", "Settings");
                return defaults;
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions)
                           ?? new AppSettings();
            settings.Normalize();
            AppLog.Info($"设置已加载：刷新 {settings.RefreshIntervalSeconds}s，阈值 {settings.LowBatteryThreshold}%", "Settings");
            return settings;
        }
        catch (Exception ex)
        {
            AppLog.Warn("设置文件读取失败，回退默认值", "Settings", ex);
            BackupCorruptSettings();
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }
    }

    private bool Save(AppSettings settings)
    {
        var tempPath = Path.Combine(
            SettingsDirectory,
            $"{SettingsFileName}.tmp-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            File.WriteAllText(tempPath, json, Utf8NoBom);
            File.Move(tempPath, SettingsPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("设置文件保存失败", ex, "Settings");
            TryDelete(tempPath);
            return false;
        }
    }

    private void BackupCorruptSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }

            var backupPath = Path.Combine(
                SettingsDirectory,
                $"{SettingsFileName}.bak-{DateTime.Now:yyyyMMddHHmmss}");
            File.Move(SettingsPath, backupPath, overwrite: true);
            AppLog.Warn($"损坏的设置文件已备份为 {Path.GetFileName(backupPath)}", "Settings");
        }
        catch (Exception ex)
        {
            AppLog.Warn("损坏的设置文件备份失败", "Settings", ex);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 临时文件清理失败不影响主流程。
        }
    }
}
