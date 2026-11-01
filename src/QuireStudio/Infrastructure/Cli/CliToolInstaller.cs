using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace QuireStudio.Infrastructure.Cli;

/// <summary>安装过程中的进度事件。</summary>
public sealed record CliInstallProgress(double Percent, string Message);

/// <summary>安装/更新结果。</summary>
public sealed record CliInstallResult
{
    public bool Succeeded { get; init; }

    /// <summary>安装后可用的启动器路径（Windows 上是 <c>quire-cli.cmd</c>）。</summary>
    public string? ExecutablePath { get; init; }

    public string? Version { get; init; }

    public string? Error { get; init; }
}

/// <summary>
/// 用纯 HTTP 从 NuGet 下载并安装 <c>quire-cli</c>。
///
/// <para>
/// 目标机器可能没有 .NET SDK / NuGet 客户端，因此这里<b>不调用 <c>dotnet tool</c></b>：
/// 直接下载 <c>.nupkg</c>（带字节进度）→ 解包出 <c>tools/&lt;tfm&gt;/any/</c> 下的程序集
/// → 生成一个调用 <c>dotnet Quire.Cli.dll</c> 的启动脚本。
/// 只需要 .NET 运行时（运行本程序的机器必然具备），不需要 SDK。
/// </para>
/// </summary>
public sealed class CliToolInstaller
{
    /// <summary>NuGet 包标识。</summary>
    public const string PackageId = "quire-cli";

    private const string FlatContainerIndexUrl = "https://api.nuget.org/v3-flatcontainer/quire-cli/index.json";

    private const double DownloadWeight = 70d;
    private const double ExtractWeight = 88d;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    /// <summary>工具安装根目录：<c>%LOCALAPPDATA%\QuireStudio\tools</c>。</summary>
    public string ToolPath { get; init; } = Path.Combine(AppPaths.Root, "tools");

    /// <summary>启动器路径：Windows 为 <c>tools\quire-cli.cmd</c>。</summary>
    public string LauncherPath => Path.Combine(
        ToolPath,
        OperatingSystem.IsWindows() ? "quire-cli.cmd" : "quire-cli");

    /// <summary>查询 NuGet 上的最新稳定版本。</summary>
    public async Task<(string? Version, string? Error)> GetLatestVersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await Http
                .GetAsync(FlatContainerIndexUrl, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return (null, $"无法访问 NuGet（HTTP {(int)response.StatusCode}）。请检查网络或代理设置。");
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("versions", out JsonElement versions) ||
                versions.ValueKind != JsonValueKind.Array)
            {
                return (null, "NuGet 返回了无法识别的版本列表。");
            }

            string? latest = null;
            Version? latestParsed = null;

            foreach (JsonElement item in versions.EnumerateArray())
            {
                string? text = item.GetString();
                if (string.IsNullOrWhiteSpace(text) || text.Contains('-', StringComparison.Ordinal))
                {
                    continue;
                }

                if (!Version.TryParse(text, out Version? parsed))
                {
                    continue;
                }

                if (latestParsed is null || parsed > latestParsed)
                {
                    latest = text;
                    latestParsed = parsed;
                }
            }

            return latest is null
                ? (null, "NuGet 上没有找到稳定版本。")
                : (latest, null);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return (null, $"检查更新失败：{exception.Message}");
        }
    }

    /// <summary>下载并安装指定版本。</summary>
    public async Task<CliInstallResult> InstallAsync(
        string version,
        IProgress<CliInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return new CliInstallResult { Error = "缺少要安装的版本号。" };
        }

        string host;

        try
        {
            host = ResolveDotnetHost()
                ?? throw new InvalidOperationException(
                    "未找到 .NET 运行时宿主（dotnet）。请先安装 .NET 10 运行时后重试。");
        }
        catch (InvalidOperationException exception)
        {
            return new CliInstallResult { Error = exception.Message };
        }

        string packagePath;

        try
        {
            packagePath = await DownloadPackageAsync(version, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return new CliInstallResult { Error = $"下载 quire-cli {version} 失败：{exception.Message}" };
        }

        try
        {
            progress?.Report(new CliInstallProgress(DownloadWeight + 2, "正在解包…"));

            string toolDirectory = Path.Combine(ToolPath, "quire-cli", version);
            TryDeleteDirectory(toolDirectory);

            string cliDll = ExtractTool(packagePath, toolDirectory, progress);

            WriteLauncher(host, cliDll, version);

            progress?.Report(new CliInstallProgress(100, $"已安装 quire-cli {version}"));

            return new CliInstallResult
            {
                Succeeded = true,
                ExecutablePath = LauncherPath,
                Version = version,
            };
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            return new CliInstallResult { Error = $"安装 quire-cli 失败：{exception.Message}" };
        }
        finally
        {
            TryDeleteFile(packagePath);
        }
    }

    private async Task<string> DownloadPackageAsync(
        string version,
        IProgress<CliInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        string url = $"https://api.nuget.org/v3-flatcontainer/{PackageId}/{version}/{PackageId}.{version}.nupkg";
        string cacheDirectory = Path.Combine(ToolPath, ".cache");
        Directory.CreateDirectory(cacheDirectory);

        string target = Path.Combine(cacheDirectory, $"{PackageId}.{version}.nupkg");

        using HttpResponseMessage response = await Http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (FileStream file = File.Create(target))
        {
            byte[] buffer = new byte[81920];
            long received = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;

                double fraction = total is > 0 ? Math.Min(1d, (double)received / total.Value) : 0d;
                progress?.Report(new CliInstallProgress(
                    fraction * DownloadWeight,
                    $"正在下载 quire-cli {version}… {FormatSize(received)}{(total is > 0 ? $" / {FormatSize(total.Value)}" : "")}"));
            }
        }

        progress?.Report(new CliInstallProgress(DownloadWeight, "下载完成"));
        return target;
    }

    /// <summary>解包 <c>tools/&lt;tfm&gt;/any</c>，返回入口程序集路径。</summary>
    private static string ExtractTool(
        string packagePath,
        string toolDirectory,
        IProgress<CliInstallProgress>? progress)
    {
        using ZipArchive archive = ZipFile.OpenRead(packagePath);

        // 定位 tools/<tfm>/any/ 前缀（工具包固定布局），注意保留结尾斜杠。
        string? prefix = null;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string[] parts = entry.FullName.Split('/');
            if (parts.Length >= 4 &&
                string.Equals(parts[0], "tools", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(parts[2], "any", StringComparison.OrdinalIgnoreCase))
            {
                prefix = $"{parts[0]}/{parts[1]}/{parts[2]}/";
                break;
            }
        }

        if (prefix is null)
        {
            throw new InvalidDataException("包内没有找到 tools/<tfm>/any 目录，可能不是 dotnet tool 包。");
        }

        List<ZipArchiveEntry> entries = archive.Entries
            .Where(entry => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrEmpty(entry.Name))
            .ToList();

        Directory.CreateDirectory(toolDirectory);

        int index = 0;
        foreach (ZipArchiveEntry entry in entries)
        {
            string relative = entry.FullName.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar);

            // 拒绝越界条目，避免把文件写到工具目录之外。
            if (relative.Length == 0 ||
                relative.Contains("..", StringComparison.Ordinal) ||
                Path.IsPathRooted(relative))
            {
                continue;
            }

            string destination = Path.Combine(toolDirectory, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);

            index++;
            progress?.Report(new CliInstallProgress(
                DownloadWeight + (ExtractWeight - DownloadWeight) * index / Math.Max(entries.Count, 1),
                $"正在解包… {index}/{entries.Count}"));
        }

        string? cliDll = Directory
            .EnumerateFiles(toolDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(file => Path.GetFileName(file).Contains("Cli", StringComparison.OrdinalIgnoreCase));

        if (cliDll is null)
        {
            throw new InvalidDataException("包内没有找到 CLI 入口程序集。");
        }

        return cliDll;
    }

    /// <summary>生成调用 <c>dotnet &lt;cli.dll&gt;</c> 的启动脚本。</summary>
    private void WriteLauncher(string host, string cliDll, string version)
    {
        Directory.CreateDirectory(ToolPath);

        string content = OperatingSystem.IsWindows()
            ? $"@echo off\r\n\"{host}\" \"{cliDll}\" %*\r\n"
            : $"#!/bin/sh\nexec \"{host}\" \"{cliDll}\" \"$@\"\n";

        File.WriteAllText(LauncherPath, content, new UTF8Encoding(false));

        // 脚本没有文件版本信息，写一个旁文件供版本检测与更新比较使用。
        File.WriteAllText(LauncherPath + ".version", version, new UTF8Encoding(false));

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                LauncherPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    private static string? ResolveDotnetHost()
    {
        // dotnet 启动工具时会设置该变量，优先使用同一套运行时。
        string? hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(hostPath) && File.Exists(hostPath))
        {
            return hostPath;
        }

        string executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", executable),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", executable),
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(directory.Trim('"'), executable);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // 忽略 PATH 里的非法片段。
            }
        }

        return null;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{bytes / 1024d:0} KB",
        _ => $"{bytes} B",
    };

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
            // 旧版本残留不影响新版本安装。
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 缓存文件删不掉不影响使用。
        }
    }
}
