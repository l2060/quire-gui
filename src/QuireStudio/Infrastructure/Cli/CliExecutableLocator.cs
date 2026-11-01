using System.Diagnostics;

namespace QuireStudio.Infrastructure.Cli;

/// <summary>
/// 定位 <c>quire-cli</c> 可执行文件。查找顺序：显式设置 → PATH → .NET 全局工具目录。
/// </summary>
public static class CliExecutableLocator
{
    public const string ExecutableName = "quire-cli";

    /// <summary>返回可执行文件的绝对路径；找不到时返回 <see langword="null"/>。</summary>
    public static string? Locate(string? preferredPath)
    {
        foreach (string candidate in EnumerateCandidates(preferredPath))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    /// <summary>读取可执行文件版本号；无法读取时返回 <see langword="null"/>。</summary>
    public static string? ReadVersion(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(executablePath);
            string? version = FirstMeaningful(info.ProductVersion, info.FileVersion);
            return version?.Split('+')[0];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateCandidates(string? preferredPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            string expanded = Environment.ExpandEnvironmentVariables(preferredPath.Trim().Trim('"'));
            foreach (string resolved in ResolveExplicit(expanded))
            {
                yield return resolved;
            }
        }

        string? fromPath = SearchPath();
        if (fromPath is not null)
        {
            yield return fromPath;
        }

        string toolsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dotnet",
            "tools");

        foreach (string fileName in PlatformFileNames())
        {
            yield return Path.Combine(toolsDirectory, fileName);
        }
    }

    private static IEnumerable<string> ResolveExplicit(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        if (Path.IsPathRooted(path) || path.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            yield return path;
            yield break;
        }

        // 只写了命令名时按 PATH 处理。
        foreach (string directory in PathDirectories())
        {
            foreach (string fileName in PlatformFileNames())
            {
                if (string.Equals(fileName, path, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileNameWithoutExtension(fileName), path, StringComparison.OrdinalIgnoreCase))
                {
                    yield return Path.Combine(directory, fileName);
                }
            }
        }
    }

    private static string? SearchPath()
    {
        foreach (string directory in PathDirectories())
        {
            foreach (string fileName in PlatformFileNames())
            {
                string candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> PathDirectories()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                yield return directory;
            }
        }
    }

    private static IEnumerable<string> PlatformFileNames()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return ExecutableName + ".exe";
            yield return ExecutableName + ".cmd";
            yield return ExecutableName + ".bat";
        }

        yield return ExecutableName;
    }

    private static string? FirstMeaningful(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
