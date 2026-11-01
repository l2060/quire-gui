using QuireStudio.Domain;

namespace QuireStudio.Infrastructure.Storage;

/// <summary>一次项目导入的结果。</summary>
public sealed record ProjectImportResult
{
    public bool Succeeded { get; init; }

    /// <summary>导入后位于工作区内的配置文件绝对路径。</summary>
    public string? ConfigPath { get; init; }

    /// <summary>复制到工作区的文件数量。</summary>
    public int CopiedFiles { get; init; }

    /// <summary>位于原项目目录之外的资源目录：复制后它们不会跟着走，需要用户自行确认。</summary>
    public IReadOnlyList<string> ExternalSources { get; init; } = [];

    public string? Error { get; init; }
}

/// <summary>
/// 把工作区之外的项目目录整体复制进工作区。
///
/// <para>
/// 复制范围：配置文件、<c>.quire</c> 状态（Tag 历史与基线索引）以及 YAML 中
/// 位于项目目录内的相对资源目录；<c>dist</c> 产物不复制（可重新打包生成）。
/// 位于项目目录之外的资源目录不会被搬动，导入后会以警告形式提示。
/// </para>
/// </summary>
public static class ProjectImportService
{
    private const string StateFolderName = ".quire";
    private const string OutputFolderName = "dist";

    /// <summary>把 <paramref name="configPath"/> 所在项目复制到工作区。</summary>
    public static ProjectImportResult Import(
        string configPath,
        string workspaceDirectory,
        ProjectConfiguration? configuration)
    {
        if (string.IsNullOrWhiteSpace(configPath) || string.IsNullOrWhiteSpace(workspaceDirectory))
        {
            return new ProjectImportResult { Error = "导入路径无效。" };
        }

        string sourceDirectory;
        try
        {
            sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? "";
        }
        catch (ArgumentException)
        {
            return new ProjectImportResult { Error = $"配置文件路径无效：{configPath}" };
        }

        if (sourceDirectory.Length == 0 || !File.Exists(configPath))
        {
            return new ProjectImportResult { Error = $"配置文件不存在：{configPath}" };
        }

        if (IsUnder(workspaceDirectory, configPath))
        {
            // 已经在工作区里，无需复制。
            return new ProjectImportResult { Succeeded = true, ConfigPath = Path.GetFullPath(configPath) };
        }

        string projectName = SanitizeName(configuration?.DisplayName, Path.GetFileNameWithoutExtension(configPath));
        string target = ResolveTarget(workspaceDirectory, projectName);

        IReadOnlyList<string> sources = configuration?.ResolveSourceDirectories(null) ?? [];
        var externalSources = new List<string>();
        var relSources = new List<string>();

        foreach (string source in sources)
        {
            if (IsUnder(sourceDirectory, source))
            {
                relSources.Add(Path.GetRelativePath(sourceDirectory, source));
            }
            else
            {
                externalSources.Add(source);
            }
        }

        int copied = 0;

        try
        {
            Directory.CreateDirectory(target);

            copied += CopyFile(configPath, Path.Combine(target, Path.GetFileName(configPath)));

            string stateSource = Path.Combine(sourceDirectory, StateFolderName);
            if (Directory.Exists(stateSource))
            {
                copied += CopyDirectory(stateSource, Path.Combine(target, StateFolderName), sourceDirectory);
            }

            foreach (string relative in relSources)
            {
                if (relative is "." or "")
                {
                    // 源目录就是项目根：只补其它顶层文件，dist 与 .quire 已单独处理。
                    copied += CopyDirectory(sourceDirectory, target, sourceDirectory);
                    continue;
                }

                string from = Path.Combine(sourceDirectory, relative);
                if (Directory.Exists(from))
                {
                    copied += CopyDirectory(from, Path.Combine(target, relative), sourceDirectory);
                }
            }

            string importedConfig = Path.Combine(target, Path.GetFileName(configPath));

            return new ProjectImportResult
            {
                Succeeded = true,
                ConfigPath = importedConfig,
                CopiedFiles = copied,
                ExternalSources = externalSources,
            };
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 复制失败时清掉半成品，避免工作区里留下残缺项目。
            TryDeleteDirectory(target);

            return new ProjectImportResult { Error = $"导入项目失败：{exception.Message}" };
        }
    }

    private static string ResolveTarget(string workspaceDirectory, string projectName)
    {
        string candidate = Path.Combine(workspaceDirectory, projectName);

        for (int index = 2; index < 100 && Directory.Exists(candidate) &&
             Directory.EnumerateFileSystemEntries(candidate).Any(); index++)
        {
            candidate = Path.Combine(workspaceDirectory, $"{projectName}-{index}");
        }

        return candidate;
    }

    private static string SanitizeName(string? preferred, string fallback)
    {
        string name = string.IsNullOrWhiteSpace(preferred) ? fallback : preferred.Trim();

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '-');
        }

        name = name.Trim().TrimEnd('.');
        return name.Length == 0 ? "project" : name;
    }

    private static bool IsUnder(string directory, string path)
    {
        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            string full = Path.GetFullPath(path);

            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Path.TrimEndingDirectorySeparator(full), root, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static int CopyFile(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        return 1;
    }

    private static int CopyDirectory(string source, string target, string projectRoot)
    {
        int copied = 0;
        Directory.CreateDirectory(target);

        foreach (string file in Directory.EnumerateFiles(source))
        {
            copied += CopyFile(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            string name = Path.GetFileName(directory);

            // 只跳过项目根下的 .quire（已单独复制）与 dist（产物可重新生成）。
            bool atRoot = string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot)),
                StringComparison.OrdinalIgnoreCase);

            if (atRoot &&
                (string.Equals(name, OutputFolderName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(name, StateFolderName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // 避免把目标目录复制进自身。
            if (IsUnder(directory, target))
            {
                continue;
            }

            copied += CopyDirectory(directory, Path.Combine(target, name), projectRoot);
        }

        return copied;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 清理失败不影响主流程。
        }
    }
}
