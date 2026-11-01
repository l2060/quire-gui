namespace QuireStudio.Infrastructure.Storage;

/// <summary>
/// 清理某个 Tag 的打包输出文件。
///
/// <para>
/// <c>quire-cli remove</c> 只删除候选记录、保留产物；当用户明确要求"一并删除"时由 GUI 补这一步。
/// 为安全起见只处理位于项目目录内的路径，并顺带删除变空的父目录。
/// </para>
/// </summary>
public static class ProjectOutputCleaner
{
    /// <summary>删除输出文件；返回成功删除数量与失败数量。</summary>
    public static (int Deleted, int Failed) Clean(string projectDirectory, IReadOnlyList<string> outputs)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory) || outputs.Count == 0)
        {
            return (0, 0);
        }

        string root;
        try
        {
            root = Path.GetFullPath(projectDirectory);
        }
        catch (ArgumentException)
        {
            return (0, outputs.Count);
        }

        int deleted = 0;
        int failed = 0;

        foreach (string output in outputs)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                continue;
            }

            string? full = Resolve(root, output);
            if (full is null)
            {
                failed++;
                continue;
            }

            try
            {
                if (File.Exists(full))
                {
                    File.Delete(full);
                    deleted++;
                }

                PruneEmptyDirectories(root, Path.GetDirectoryName(full));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failed++;
            }
        }

        return (deleted, failed);
    }

    private static string? Resolve(string root, string output)
    {
        try
        {
            string full = Path.GetFullPath(
                Path.IsPathRooted(output) ? output : Path.Combine(root, output));

            // 只允许删除项目目录内的文件，避免"../../"式的越界删除。
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>自下而上删除空目录，遇到非空目录或项目根目录即停止。</summary>
    private static void PruneEmptyDirectories(string root, string? directory)
    {
        while (!string.IsNullOrWhiteSpace(directory) &&
               !string.Equals(directory.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory) || Directory.EnumerateFileSystemEntries(directory).Any())
            {
                return;
            }

            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }
}
