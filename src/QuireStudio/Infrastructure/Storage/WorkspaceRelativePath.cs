namespace QuireStudio.Infrastructure.Storage;

/// <summary>
/// 登记表里路径的存储格式：工作区内的配置文件按<b>相对路径</b>保存，
/// 这样整个工作区目录被移动或改名后，登记表依然指向正确的位置。
/// </summary>
public static class WorkspaceRelativePath
{
    /// <summary>
    /// 把绝对路径转成写进 <c>projects.json</c> 的形式。
    /// 位于工作区内时返回相对路径（正斜杠分隔），否则保留绝对路径。
    /// </summary>
    public static string ToStored(string? workspaceRoot, string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return "";
        }

        string normalized = absolutePath.Replace('\\', '/');

        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return normalized;
        }

        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
            string full = Path.GetFullPath(absolutePath);

            string prefix = root + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            string relative = Path.GetRelativePath(root, full).Replace('\\', '/');
            return relative;
        }
        catch (ArgumentException)
        {
            return normalized;
        }
    }

    /// <summary>
    /// 把登记表里的路径还原成绝对路径。
    /// 历史数据可能存的是绝对路径，这里一并兼容。
    /// </summary>
    public static string? Resolve(string? workspaceRoot, string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return null;
        }

        string path = storedPath.Trim().Replace('/', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(Path.Combine(workspaceRoot, path));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
