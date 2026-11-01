namespace QuireStudio.Infrastructure.Storage;

/// <summary>
/// 把用户挑选的目录转换成写进配置文件的路径写法。
/// </summary>
public static class ProjectPathFormatter
{
    /// <summary>
    /// 由用户选中的绝对目录生成 <c>output</c> 的值。
    ///
    /// <para>
    /// 目录位于项目内时改写为 <c>./相对路径</c>，便于整个项目搬移；
    /// 项目外则保留绝对路径。<paramref name="keepTagPlaceholder"/> 为真时
    /// 保留 <c>{tag}</c> 占位符，避免多次打包互相覆盖。
    /// </para>
    /// </summary>
    public static string ToOutputTemplate(string configDirectory, string pickedDirectory, bool keepTagPlaceholder)
    {
        string normalized = ToProjectRelative(configDirectory, pickedDirectory);

        return keepTagPlaceholder
            ? $"{normalized.TrimEnd('/')}/{{tag}}"
            : normalized;
    }

    /// <summary>项目内目录返回 <c>./相对路径</c>，否则返回用正斜杠规范化的绝对路径。</summary>
    public static string ToProjectRelative(string configDirectory, string pickedDirectory)
    {
        string fallback = pickedDirectory.Replace('\\', '/');

        if (string.IsNullOrWhiteSpace(configDirectory) || string.IsNullOrWhiteSpace(pickedDirectory))
        {
            return fallback;
        }

        try
        {
            string root = Path.GetFullPath(configDirectory);
            string full = Path.GetFullPath(pickedDirectory);

            if (!full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(Path.TrimEndingDirectorySeparator(full), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
            {
                return fallback;
            }

            string relative = Path.GetRelativePath(root, full).Replace('\\', '/');
            return relative is "." or "" ? "./" : $"./{relative}";
        }
        catch (ArgumentException)
        {
            return fallback;
        }
    }

    /// <summary>把 <c>output</c> 里的占位符/相对路径还原成用于选择框起始位置的绝对目录。</summary>
    public static string? ResolveBaseDirectory(string? configDirectory, string? output)
    {
        if (string.IsNullOrWhiteSpace(configDirectory) || string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        string withoutTag = output.Replace("{tag}", "", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/', '\\');

        if (withoutTag.Length == 0)
        {
            return null;
        }

        try
        {
            string full = Path.IsPathRooted(withoutTag)
                ? Path.GetFullPath(withoutTag)
                : Path.GetFullPath(Path.Combine(configDirectory, withoutTag));

            return full;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
