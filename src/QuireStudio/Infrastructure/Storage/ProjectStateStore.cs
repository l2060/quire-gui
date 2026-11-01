namespace QuireStudio.Infrastructure.Storage;

/// <summary>
/// 打包状态目录（<c>.quire/&lt;project&gt;</c>）的维护。
///
/// <para>
/// CLI 把 Tag 历史与基线索引按项目名放在 <c>.quire/&lt;project&gt;/</c> 下。
/// 因此在 GUI 里改项目名时，除了写回 YAML，还必须同步重命名这个目录，
/// 否则历史会被当成"另一个项目"而从界面里消失。
/// </para>
/// </summary>
public static class ProjectStateStore
{
    private const string StateFolderName = ".quire";

    /// <summary>
    /// 把状态目录从旧项目名改为新项目名。
    /// 成功或无需处理时返回 <see langword="null"/>，失败时返回可展示的错误信息。
    /// </summary>
    public static string? Rename(string configPath, string? oldName, string? newName)
    {
        if (string.IsNullOrWhiteSpace(configPath) ||
            string.IsNullOrWhiteSpace(newName) ||
            string.IsNullOrWhiteSpace(oldName))
        {
            return null;
        }

        string? directory = Path.GetDirectoryName(configPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        string root = Path.Combine(directory, StateFolderName);
        if (!Directory.Exists(root))
        {
            return null;
        }

        string source = Path.Combine(root, oldName.Trim());
        string target = Path.Combine(root, newName.Trim());

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!Directory.Exists(source))
        {
            // 没有旧状态（例如从未打包过），无需迁移。
            return null;
        }

        if (Directory.Exists(target))
        {
            return $"状态目录 {target} 已存在，项目名没有改写，避免覆盖已有的 Tag 历史。";
        }

        try
        {
            Directory.Move(source, target);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"重命名状态目录失败：{exception.Message}";
        }
    }
}
