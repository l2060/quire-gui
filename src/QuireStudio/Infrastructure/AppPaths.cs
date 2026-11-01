namespace QuireStudio.Infrastructure;

/// <summary>
/// 应用数据目录与工作区路径解析。
///
/// <para>
/// 应用数据目录（<see cref="Root"/>）只保存 <c>settings.json</c>；
/// 工作区由用户显式选择（首次启动或右键菜单/--workspace 指定），
/// 程序不会擅自创建或使用任何默认工作区。
/// </para>
///
/// <para>
/// 启动时会探测数据目录是否可写：不可写（受限会话、只读安装目录等）时依次回退到
/// 临时目录、程序目录下的 <c>.quirestudio</c>，并通过 <see cref="StartupWarning"/>
/// 告知用户，绝不因为写不了文件而崩溃。
/// </para>
/// </summary>
public static class AppPaths
{
    /// <summary>覆盖应用数据目录的环境变量名。</summary>
    public const string HomeEnvironmentVariable = "QUIRESTUDIO_HOME";

    private const string ProductFolderName = "QuireStudio";
    private const string RegistryFileName = "projects.json";
    private const string LocalDataFolderName = ".quirestudio";

    private static bool _initialized;

    /// <summary>应用数据根目录，例如 <c>%LOCALAPPDATA%\QuireStudio</c>。</summary>
    public static string Root { get; private set; } = ResolveRoot();

    /// <summary>启动时发现数据目录不可写的原因；为空表示一切正常。</summary>
    public static string? StartupWarning { get; private set; }

    /// <summary>应用设置文件。</summary>
    public static string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary>返回指定工作区的项目登记表路径。</summary>
    public static string RegistryFile(string workspaceDirectory) =>
        Path.Combine(workspaceDirectory, RegistryFileName);

    /// <summary>
    /// 选择可用的数据目录：优先配置值，其次临时目录，最后程序目录。
    /// 只执行一次，永不抛出。
    /// </summary>
    public static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        string preferred = Root;
        if (TryPrepare(preferred, out string? preferredError))
        {
            return;
        }

        string[] fallbacks =
        [
            Path.Combine(Path.GetTempPath(), ProductFolderName),
            Path.Combine(AppContext.BaseDirectory, LocalDataFolderName),
        ];

        foreach (string candidate in fallbacks)
        {
            if (string.Equals(candidate, preferred, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryPrepare(candidate, out _))
            {
                continue;
            }

            Root = candidate;
            StartupWarning =
                $"无法写入 {preferred}（{preferredError}），设置已临时保存在 {candidate}。该位置位于程序目录，重新构建可能被清理；如需长期保留，请用 --home <目录> 或环境变量 {HomeEnvironmentVariable} 指定可写目录。";
            return;
        }

        StartupWarning =
            $"无法写入任何数据目录（{preferred}：{preferredError}）。本次会话的工作区与项目不会被记住；请检查目录权限或设置 {HomeEnvironmentVariable}。";
    }

    /// <summary>规范化目录路径：去掉尾部反斜杠；空白或非法路径返回 <see langword="null"/>。</summary>
    public static string? Normalize(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        try
        {
            string full = Path.GetFullPath(folder.Trim().Trim('"'));
            return Path.TrimEndingDirectorySeparator(full);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>确保应用数据目录存在；失败时静默忽略，避免影响启动。</summary>
    public static void EnsureCreated()
    {
        try
        {
            Directory.CreateDirectory(Root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 目录不可用时由 StartupWarning 提示，不影响程序继续运行。
        }
    }

    /// <summary>确保工作区目录存在。</summary>
    public static void EnsureWorkspace(string workspaceDirectory) =>
        Directory.CreateDirectory(workspaceDirectory);

    private static bool TryPrepare(string directory, out string? error)
    {
        try
        {
            Directory.CreateDirectory(directory);

            string probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():n}");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            error = null;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            error = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private static string ResolveRoot()
    {
        string? overridden = Environment.GetEnvironmentVariable(HomeEnvironmentVariable);
        if (Normalize(overridden) is { } normalized)
        {
            return normalized;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductFolderName);
    }
}
