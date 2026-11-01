using QuireStudio.Domain;
using QuireStudio.Infrastructure.Configuration;
using QuireStudio.Infrastructure.Storage;

namespace QuireStudio.Infrastructure;

/// <summary>
/// 工作区初始化：当工作区还没有登记表时，自动把根目录下的
/// <c>quire*.yml</c> / <c>quire*.yaml</c> 登记为项目。
///
/// <para>
/// 这样从资源管理器右键「在 Quire Studio 中打开」一个已有工程目录时，
/// 该目录会直接成为工作区，其配置也被识别出来。
/// </para>
/// </summary>
public static class WorkspaceProjectImporter
{
    private static readonly string[] ConfigurationPatterns = ["quire*.yml", "quire*.yaml"];

    private static readonly string[] SkippedDirectories = [".git", ".quire", "bin", "obj", "node_modules", "dist"];

    /// <summary>扫描并登记工作区根目录及其一级子目录下的配置，返回新增数量。</summary>
    public static int Import(
        string workspaceDirectory,
        ProjectRegistry registry,
        ProjectConfigurationReader reader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(reader);

        if (!Directory.Exists(workspaceDirectory))
        {
            return 0;
        }

        int added = ImportFrom(workspaceDirectory, registry, reader);

        // 「打开一个目录作为工作区」时，工程通常位于一级子目录中。
        foreach (string directory in Directory.EnumerateDirectories(workspaceDirectory))
        {
            string name = Path.GetFileName(directory);
            if (SkippedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase) || name.StartsWith('.'))
            {
                continue;
            }

            added += ImportFrom(directory, registry, reader);
        }

        if (added > 0)
        {
            registry.Save();
        }

        return added;
    }

    private static int ImportFrom(
        string directory,
        ProjectRegistry registry,
        ProjectConfigurationReader reader)
    {
        int added = 0;

        foreach (string pattern in ConfigurationPatterns)
        {
            foreach (string file in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
            {
                if (registry.FindByConfigPath(file) is not null)
                {
                    continue;
                }

                ProjectConfigurationLoadResult result = reader.Read(file);
                if (!result.Succeeded || result.Configuration is null)
                {
                    continue;
                }

                registry.Register(new ProjectRecord
                {
                    Name = result.Configuration.DisplayName,
                    ConfigPath = Path.GetFullPath(file),
                    IsManaged = false,
                });

                added++;
            }
        }

        return added;
    }
}
