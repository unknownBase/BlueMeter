using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BlueMeter.Services;

/// <summary>
/// 轻量文件日志：按天写入、单文件 5 MB 滚动、保留 7 天。任何日志失败都不得影响主流程。
/// </summary>
public static class AppLog
{
    private const int RetentionDays = 7;
    private const long MaxFileBytes = 5 * 1024 * 1024;

    private static readonly object SyncRoot = new();
    private static readonly Regex MacAddressRegex = new(
        @"(?i)\b(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string UserProfilePath = Environment.GetFolderPath(
        Environment.SpecialFolder.UserProfile);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BlueMeter",
        "logs");

    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            DeleteExpiredLogs();
            Info($"BlueMeter 启动，版本 {typeof(AppLog).Assembly.GetName().Version}", "App");
        }
        catch
        {
            // 日志不可用不能阻止应用启动。
        }
    }

    public static void Debug(string message, string category = "App") =>
        Write("DEBUG", message, category, exception: null);

    public static void Info(string message, string category = "App") =>
        Write("INFO", message, category, exception: null);

    public static void Warn(string message, string category = "App", Exception? exception = null) =>
        Write("WARN", message, category, exception);

    public static void Error(string message, Exception? exception = null, string category = "App") =>
        Write("ERROR", message, category, exception);

    private static void Write(string level, string message, string category, Exception? exception)
    {
        try
        {
            var now = DateTime.Now;
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{category}] {MaskSensitiveData(message)}");

            if (exception is not null)
            {
                line += Environment.NewLine + MaskSensitiveData(exception.ToString());
            }

            line += Environment.NewLine;

            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);
                var path = Path.Combine(
                    LogDirectory,
                    $"bluemeter-{now:yyyyMMdd}.log");

                RollIfNeeded(path);
                File.AppendAllText(path, line, Utf8NoBom);
            }
        }
        catch
        {
            // 日志是尽力而为的附属能力。
        }
    }

    private static void RollIfNeeded(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length < MaxFileBytes)
            {
                return;
            }

            var rolledPath = path + ".1";
            File.Move(path, rolledPath, overwrite: true);
        }
        catch
        {
            // 滚动失败时继续追加，避免丢日志。
        }
    }

    private static void DeleteExpiredLogs()
    {
        try
        {
            var threshold = DateTime.Now.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(LogDirectory, "bluemeter-*.log*"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < threshold)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // 单个文件被占用时忽略。
                }
            }
        }
        catch
        {
            // 清理失败不影响启动。
        }
    }

    private static string MaskSensitiveData(string value)
    {
        if (!string.IsNullOrEmpty(UserProfilePath))
        {
            value = value.Replace(
                UserProfilePath,
                "%USERPROFILE%",
                StringComparison.OrdinalIgnoreCase);
        }

        return MacAddressRegex.Replace(value, match =>
        {
            var parts = Regex.Split(match.Value, "[:-]");
            return parts.Length == 6
                ? $"{parts[0]}:{parts[1]}:{parts[2]}:**:**:**"
                : "**:****";
        });
    }
}
