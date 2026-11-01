using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

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

    /// <summary>dotnet 全局工具目录（<c>dotnet tool install -g</c> 的落点）。</summary>
    public static string GlobalToolDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".dotnet",
        "tools");

    /// <summary>判断可执行文件是否来自 dotnet 全局工具安装。</summary>
    public static bool IsGlobalTool(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(GlobalToolDirectory));
            string full = Path.GetFullPath(executablePath);

            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>判断可执行文件是否由 QuireStudio 自己下载维护。</summary>
    public bool IsManaged(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ToolPath));
            string full = Path.GetFullPath(executablePath);

            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>本机是否装了 .NET SDK（<c>dotnet tool</c> 需要它；只有运行时不够）。</summary>
    public async Task<bool> HasDotnetSdkAsync(CancellationToken cancellationToken = default)
    {
        if (ResolveDotnetHost() is not { } host)
        {
            return false;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            (int exitCode, string output) = await RunAsync(host, ["--list-sdks"], timeout.Token).ConfigureAwait(false);
            return exitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// 原地升级 dotnet 全局工具。仅当本机有 SDK 且 CLI 确实装在全局工具目录时可用。
    /// </summary>
    public async Task<CliInstallResult> UpdateGlobalToolAsync(
        string version,
        IProgress<CliInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (ResolveDotnetHost() is not { } host)
        {
            return new CliInstallResult { Error = "未找到 dotnet 命令，无法升级全局工具。" };
        }

        progress?.Report(new CliInstallProgress(5, $"正在用 dotnet tool 升级到 {version}…"));

        (int exitCode, string output) = await RunAsync(
            host,
            ["tool", "update", "-g", PackageId, "--version", version],
            cancellationToken).ConfigureAwait(false);

        string globalExecutable = Path.Combine(
            GlobalToolDirectory,
            OperatingSystem.IsWindows() ? $"{PackageId}.exe" : PackageId);

        if (exitCode != 0)
        {
            return new CliInstallResult
            {
                Error = string.IsNullOrWhiteSpace(output)
                    ? $"升级全局工具失败（dotnet 退出码 {exitCode}）。"
                    : $"升级全局工具失败：{FirstLine(output)}",
            };
        }

        progress?.Report(new CliInstallProgress(100, $"全局工具已升级到 {version}"));

        return new CliInstallResult
        {
            Succeeded = true,
            ExecutablePath = globalExecutable,
            Version = version,
        };
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
            return await InstallFromPackageAsync(packagePath, version, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteFile(packagePath);
        }
    }

    /// <summary>
    /// 从已下载的 <c>.nupkg</c> 安装（与下载分离，便于离线安装与测试）。
    ///
    /// <para>
    /// 兼容两种 CLI 形态：
    /// 原生可执行文件（AOT / 自带 apphost）→ 直接使用它，不再需要 dotnet；
    /// 只有托管程序集（框架依赖）→ 生成调用 <c>dotnet &lt;dll&gt;</c> 的启动脚本。
    /// </para>
    /// </summary>
    public Task<CliInstallResult> InstallFromPackageAsync(
        string packagePath,
        string version,
        IProgress<CliInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath))
        {
            return Task.FromResult(new CliInstallResult { Error = $"安装包不存在：{packagePath}" });
        }

        try
        {
            string toolDirectory = Path.Combine(ToolPath, "quire-cli", version);
            TryDeleteDirectory(toolDirectory);

            ToolEntry entry = ExtractTool(packagePath, toolDirectory, progress);

            string executablePath;

            if (entry.NativeExecutable is { } native)
            {
                // 原生 exe：直接运行，不需要 dotnet，也不需要启动脚本。
                File.WriteAllText(native + ".version", version, new UTF8Encoding(false));
                executablePath = native;
            }
            else if (entry.ManagedAssembly is { } assembly && ResolveDotnetHost() is { } host)
            {
                WriteLauncher(host, assembly, version);
                executablePath = LauncherPath;
            }
            else
            {
                return Task.FromResult(new CliInstallResult
                {
                    Error = entry.ManagedAssembly is null
                        ? "包内没有找到 quire-cli 可执行文件。"
                        : "包内是框架依赖版本，但本机没有可用的 .NET 运行时宿主（dotnet）。",
                });
            }

            progress?.Report(new CliInstallProgress(100, $"已安装 quire-cli {version}"));

            return Task.FromResult(new CliInstallResult
            {
                Succeeded = true,
                ExecutablePath = executablePath,
                Version = version,
            });
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            return Task.FromResult(new CliInstallResult { Error = $"安装 quire-cli 失败：{exception.Message}" });
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

    /// <summary>
    /// 解包 <c>tools/&lt;tfm&gt;/any</c> 并识别入口：
    /// 优先原生可执行文件（AOT / 自带 apphost），其次托管程序集。
    /// </summary>
    private static ToolEntry ExtractTool(
        string packagePath,
        string toolDirectory,
        IProgress<CliInstallProgress>? progress)
    {
        using ZipArchive archive = ZipFile.OpenRead(packagePath);

        string? prefix = null;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string[] parts = entry.FullName.Split('/');
            // 兼容 tools/<tfm>/any（托管）与 tools/<tfm>/<rid>（AOT 按 RID 打包）。
            if (parts.Length >= 4 &&
                string.Equals(parts[0], "tools", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(parts[2], "any", StringComparison.OrdinalIgnoreCase) ||
                 parts[2].Length > 0))
            {
                prefix = $"{parts[0]}/{parts[1]}/{parts[2]}/";
                break;
            }
        }

        if (prefix is null)
        {
            throw new InvalidDataException("包内没有找到 tools/<tfm>/ 目录，可能不是 dotnet tool 包。");
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

        return DetectEntry(toolDirectory);
    }

    /// <summary>在解包目录里找出可执行入口。</summary>
    private static ToolEntry DetectEntry(string toolDirectory)
    {
        string command = "quire-cli";

        // dotnet tool 包会声明命令名，AOT 产物通常就叫这个名字。
        string settings = Path.Combine(toolDirectory, "DotnetToolSettings.xml");
        if (File.Exists(settings))
        {
            try
            {
                XDocument document = XDocument.Load(settings);
                string? name = document.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "Command")?
                    .Attribute("Name")?.Value;

                if (!string.IsNullOrWhiteSpace(name))
                {
                    command = name.Trim();
                }
            }
            catch (Exception exception) when (exception is IOException or System.Xml.XmlException)
            {
                // 声明文件读不出来就退回默认命令名。
            }
        }

        string[] nativeCandidates = OperatingSystem.IsWindows()
            ? [$"{command}.exe", "Quire.Cli.exe", "quire.exe"]
            : [command, "Quire.Cli", "quire"];

        foreach (string candidate in nativeCandidates)
        {
            string path = Path.Combine(toolDirectory, candidate);
            if (File.Exists(path))
            {
                return new ToolEntry(path, null);
            }
        }

        // 兜底：如果目录里只有一个可执行文件，就认为它是入口。
        string[] executables = OperatingSystem.IsWindows()
            ? Directory.GetFiles(toolDirectory, "*.exe", SearchOption.TopDirectoryOnly)
            : [];

        if (executables.Length == 1)
        {
            return new ToolEntry(executables[0], null);
        }

        string? assembly = Directory
            .EnumerateFiles(toolDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(file => Path.GetFileName(file).Contains("Cli", StringComparison.OrdinalIgnoreCase));

        return assembly is null ? new ToolEntry(null, null) : new ToolEntry(null, assembly);
    }

    /// <summary>安装产物：原生可执行文件与托管程序集二选一。</summary>
    private sealed record ToolEntry(string? NativeExecutable, string? ManagedAssembly);
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

    /// <summary>运行一个子进程（用于 <c>dotnet tool</c> 与 SDK 探测）。</summary>
    private static async Task<(int ExitCode, string Output)> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
        {
            return (-1, "无法启动 dotnet 进程。");
        }

        Task<string> stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        string output = (await stdout.ConfigureAwait(false)) + (await stderr.ConfigureAwait(false));
        return (process.ExitCode, output.Trim());
    }

    private static string FirstLine(string text) =>
        text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? text.Trim();

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
