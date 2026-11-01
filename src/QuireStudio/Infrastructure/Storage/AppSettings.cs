namespace QuireStudio.Infrastructure.Storage;

/// <summary>应用级设置：只保存与具体项目无关的全局状态。</summary>
public sealed class AppSettings
{
    /// <summary>最近使用的工作区条数上限。</summary>
    public const int MaxRecentWorkspaces = 10;

    /// <summary>显式指定的 <c>quire-cli</c> 可执行文件路径；为空时自动探测。</summary>
    public string? CliExecutablePath { get; set; }

    /// <summary>
    /// 上次关闭时使用的工作区。直接启动时沿用它；
    /// 为空表示尚未选择工作区，界面会先让用户挑一个目录。
    /// </summary>
    public string? WorkspaceDirectory { get; set; }

    /// <summary>历史工作区，最近使用在前，最多 <see cref="MaxRecentWorkspaces"/> 条。</summary>
    public List<string> RecentWorkspaces { get; set; } = [];

    /// <summary>界面主题：<c>system</c>（跟随系统）、<c>light</c> 或 <c>dark</c>。</summary>
    public string ThemeMode { get; set; } = "system";
}

/// <summary>应用设置的读写入口。</summary>
public static class SettingsStore
{
    public static AppSettings Load(string? filePath = null)
    {
        string path = string.IsNullOrWhiteSpace(filePath) ? AppPaths.SettingsFile : filePath;
        AppSettings settings = JsonFileStore.Read<AppSettings>(path) ?? new AppSettings();

        // 统一规范化：去掉尾部反斜杠并按路径去重，避免同一个工作区出现两条历史。
        settings.WorkspaceDirectory = AppPaths.Normalize(settings.WorkspaceDirectory);
        settings.RecentWorkspaces = (settings.RecentWorkspaces ?? [])
            .Select(AppPaths.Normalize)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(AppSettings.MaxRecentWorkspaces)
            .ToList();

        return settings;
    }

    public static bool Save(AppSettings settings, out string? error, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string path = string.IsNullOrWhiteSpace(filePath) ? AppPaths.SettingsFile : filePath;

        try
        {
            JsonFileStore.Write(path, settings);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    public static bool Save(AppSettings settings, string? filePath = null) =>
        Save(settings, out _, filePath);
}
