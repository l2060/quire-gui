using QuireStudio.Domain;

namespace QuireStudio.Infrastructure.Cli;

/// <summary>
/// <c>quire-cli</c> 的类型化调用封装。集中维护每个命令的参数拼写，
/// 使界面层只关心领域对象，不直接接触命令行语法。
/// </summary>
public sealed class QuireCliClient
{
    /// <summary>查询与轻量命令的看门狗时限；打包命令不设限，由用户主动取消。</summary>
    private static readonly TimeSpan ShortCommandTimeout = TimeSpan.FromSeconds(60);

    private readonly CliProcessRunner _runner;

    public QuireCliClient(CliProcessRunner? runner = null)
    {
        _runner = runner ?? new CliProcessRunner();
    }

    /// <summary>用户在设置中指定的可执行文件路径。</summary>
    public string? PreferredExecutablePath { get; set; }

    /// <summary>实际使用的可执行文件绝对路径。</summary>
    public string? ExecutablePath { get; private set; }

    /// <summary>从可执行文件读取的版本号。</summary>
    public string? Version { get; private set; }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(ExecutablePath) && File.Exists(ExecutablePath);

    /// <summary>重新探测可执行文件。</summary>
    public bool Refresh()
    {
        ExecutablePath = CliExecutableLocator.Locate(PreferredExecutablePath);
        Version = CliExecutableLocator.ReadVersion(ExecutablePath);
        return IsAvailable;
    }

    /// <summary>执行任意子命令。</summary>
    public Task<CliResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        Action<CliEvent>? onEvent = null,
        string? displayCommand = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        if (ExecutablePath is null)
        {
            return Task.FromResult(CliResult.NotStarted(
                "未找到 quire-cli，请在设置中指定可执行文件路径。",
                arguments,
                displayCommand));
        }

        return _runner.RunAsync(
            ExecutablePath,
            arguments,
            workingDirectory,
            onEvent,
            displayCommand,
            cancellationToken,
            timeout);
    }

    // ---------------------------------------------------------------------
    // 项目与历史
    // ---------------------------------------------------------------------

    /// <summary>读取项目的全部 Tag 记录。</summary>
    public async Task<CliOutcome<IReadOnlyList<QuireTag>>> GetTagsAsync(
        ProjectRecord project,
        Action<CliEvent>? onEvent = null,
        CancellationToken cancellationToken = default)
    {
        CliResult result = await RunAsync(
            BuildHistoryArgs("tags", project),
            project.Directory,
            onEvent,
            cancellationToken: cancellationToken,
            timeout: ShortCommandTimeout).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return CliOutcome<IReadOnlyList<QuireTag>>.Failure(Describe(result), result);
        }

        IReadOnlyList<QuireTag> tags = result.Items
            .Where(item => string.Equals(item.ItemKind, "tag", StringComparison.Ordinal))
            .Select(item => CliJsonParser.ToTag(item.Item))
            .ToArray();

        return CliOutcome<IReadOnlyList<QuireTag>>.Success(tags, result);
    }

    /// <summary>读取某个 Tag 在构建时记录的文件变化。</summary>
    public async Task<CliOutcome<IReadOnlyList<QuireChange>>> GetTagChangesAsync(
        ProjectRecord project,
        string tagName,
        Action<CliEvent>? onEvent = null,
        CancellationToken cancellationToken = default)
    {
        CliResult result = await RunAsync(
            BuildHistoryArgs("changes", project, tagName),
            project.Directory,
            onEvent,
            cancellationToken: cancellationToken,
            timeout: ShortCommandTimeout).ConfigureAwait(false);

        return ToChangeOutcome(result);
    }

    /// <summary>对比当前源文件与正式基线（<c>diff</c>）。</summary>
    public async Task<CliOutcome<IReadOnlyList<QuireChange>>> DiffAsync(
        ProjectRecord project,
        Action<CliEvent>? onEvent = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "diff" };
        AppendConfig(arguments, project);
        AppendResourceRoot(arguments, project);
        arguments.Add("-progress");

        CliResult result = await RunAsync(
            arguments,
            project.Directory,
            onEvent,
            cancellationToken: cancellationToken,
            timeout: ShortCommandTimeout).ConfigureAwait(false);

        return ToChangeOutcome(result);
    }

    /// <summary>构建候选；不会推进基线。</summary>
    public async Task<CliOutcome<QuireTag>> PackageAsync(
        ProjectRecord project,
        string? tagName,
        Action<CliEvent>? onEvent = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "package" };
        AppendConfig(arguments, project);

        if (!string.IsNullOrWhiteSpace(tagName))
        {
            arguments.Add("-tag");
            arguments.Add(tagName.Trim());
        }

        // 线程数由配置文件的 build.jobs 决定，GUI 不再用 -jobs 覆盖。
        AppendResourceRoot(arguments, project);
        arguments.Add("-progress");

        CliResult result = await RunAsync(
            arguments,
            project.Directory,
            onEvent,
            cancellationToken: cancellationToken,
            timeout: ShortCommandTimeout).ConfigureAwait(false);

        return ToTagOutcome(result);
    }

    /// <summary>把候选提交为正式基线。</summary>
    public async Task<CliOutcome<QuireTag>> CommitAsync(
        ProjectRecord project,
        string tagName,
        Action<CliEvent>? onEvent = null,
        CancellationToken cancellationToken = default)
    {
        CliResult result = await RunAsync(
            BuildHistoryArgs("commit", project, tagName),
            project.Directory,
            onEvent,
            cancellationToken: cancellationToken,
            timeout: ShortCommandTimeout).ConfigureAwait(false);

        return ToTagOutcome(result);
    }

    /// <summary>放弃未提交的候选，保留输出文件。</summary>
    public async Task<CliOutcome<QuireTag>> DiscardAsync(
        ProjectRecord project,
        string tagName,
        Action<CliEvent>? onEvent = null,
        CancellationToken cancellationToken = default)
    {
        CliResult result = await RunAsync(
            BuildHistoryArgs("remove", project, tagName),
            project.Directory,
            onEvent,
            cancellationToken: cancellationToken,
            timeout: ShortCommandTimeout).ConfigureAwait(false);

        return ToTagOutcome(result);
    }

    /// <summary>创建项目骨架，返回生成的配置文件绝对路径。</summary>
    public async Task<CliOutcome<string>> InitProjectAsync(
        string projectName,
        string directory,
        Action<CliEvent>? onEvent = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> arguments = ["init", projectName, directory, "-progress"];
        CliResult result = await RunAsync(
            arguments,
            directory,
            onEvent,
            cancellationToken: cancellationToken,
            timeout: ShortCommandTimeout).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return CliOutcome<string>.Failure(Describe(result), result);
        }

        string? path = CliJsonParser.ReadResultString(result.Result, "path");
        return string.IsNullOrWhiteSpace(path)
            ? CliOutcome<string>.Failure("init 未返回配置文件路径。", result)
            : CliOutcome<string>.Success(path, result);
    }

    // ---------------------------------------------------------------------
    // 全局密钥
    // ---------------------------------------------------------------------

    /// <summary>列出本机全部命名密钥。</summary>
    public async Task<CliOutcome<IReadOnlyList<QuireKey>>> GetKeysAsync(
        Action<CliEvent>? onEvent = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> arguments = ["keys", "list", "-progress"];
        CliResult result = await RunAsync(arguments, null, onEvent, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return CliOutcome<IReadOnlyList<QuireKey>>.Failure(Describe(result), result);
        }

        IReadOnlyList<QuireKey> keys = result.Items
            .Where(item => string.Equals(item.ItemKind, "key", StringComparison.Ordinal))
            .Select(item => CliJsonParser.ToKey(item.Item))
            .ToArray();

        return CliOutcome<IReadOnlyList<QuireKey>>.Success(keys, result);
    }

    /// <summary>新增命名密钥。<paramref name="password"/> 不会出现在日志或命令行展示中。</summary>
    public async Task<CliOutcome<QuireKey>> AddKeyAsync(
        string name,
        string password,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> arguments = ["keys", "add", name, password, "-progress"];
        CliResult result = await RunAsync(
            arguments,
            displayCommand: $"quire-cli keys add {name} ••••••••",
            cancellationToken: cancellationToken,
            timeout: ShortCommandTimeout).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return CliOutcome<QuireKey>.Failure(Describe(result), result);
        }

        return CliOutcome<QuireKey>.Success(
            new QuireKey
            {
                Name = name,
                Fingerprint = CliJsonParser.ReadResultString(result.Result, "fingerprint") ?? "",
                ClientKey = "",
            },
            result);
    }

    /// <summary>读取单个密钥的客户端密钥字符串。</summary>
    public async Task<CliOutcome<string>> ShowKeyAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> arguments = ["keys", "show", name, "-progress"];
        CliResult result = await RunAsync(arguments, cancellationToken: cancellationToken,
            timeout: ShortCommandTimeout).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return CliOutcome<string>.Failure(Describe(result), result);
        }

        string? clientKey = CliJsonParser.ReadResultString(result.Result, "clientKey");
        return string.IsNullOrWhiteSpace(clientKey)
            ? CliOutcome<string>.Failure("keys show 未返回客户端密钥。", result)
            : CliOutcome<string>.Success(clientKey, result);
    }

    /// <summary>删除本机密钥条目。</summary>
    public async Task<CliOutcome<string>> RemoveKeyAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> arguments = ["keys", "remove", name, "-progress"];
        CliResult result = await RunAsync(arguments, cancellationToken: cancellationToken,
            timeout: ShortCommandTimeout).ConfigureAwait(false);

        return result.Succeeded
            ? CliOutcome<string>.Success(name, result)
            : CliOutcome<string>.Failure(Describe(result), result);
    }

    /// <summary>导出二进制客户端密钥文件，返回文件绝对路径。</summary>
    public async Task<CliOutcome<string>> ExportKeyAsync(
        string name,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> arguments = ["keys", "export", name, destinationPath, "-progress"];
        CliResult result = await RunAsync(arguments, cancellationToken: cancellationToken,
            timeout: ShortCommandTimeout).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return CliOutcome<string>.Failure(Describe(result), result);
        }

        string? path = CliJsonParser.ReadResultString(result.Result, "path");
        return CliOutcome<string>.Success(string.IsNullOrWhiteSpace(path) ? destinationPath : path, result);
    }

    // ---------------------------------------------------------------------
    // 参数拼装
    // ---------------------------------------------------------------------

    private static IReadOnlyList<string> BuildHistoryArgs(string command, ProjectRecord project, string? tagName = null)
    {
        var arguments = new List<string> { command };
        if (tagName is not null)
        {
            arguments.Add(tagName);
        }

        AppendConfig(arguments, project);
        arguments.Add("-progress");
        return arguments;
    }

    private static void AppendConfig(List<string> arguments, ProjectRecord project)
    {
        arguments.Add("-f");
        arguments.Add(project.ConfigPath);
    }

    private static void AppendResourceRoot(List<string> arguments, ProjectRecord project)
    {
        if (!string.IsNullOrWhiteSpace(project.ResourceRoot))
        {
            arguments.Add("-root");
            arguments.Add(project.ResourceRoot);
        }
    }

    private static CliOutcome<IReadOnlyList<QuireChange>> ToChangeOutcome(CliResult result)
    {
        if (!result.Succeeded)
        {
            return CliOutcome<IReadOnlyList<QuireChange>>.Failure(Describe(result), result);
        }

        IReadOnlyList<QuireChange> changes = result.Items
            .Where(item => string.Equals(item.ItemKind, "change", StringComparison.Ordinal))
            .Select(item => CliJsonParser.ToChange(item.Item))
            .ToArray();

        return CliOutcome<IReadOnlyList<QuireChange>>.Success(changes, result);
    }

    private static CliOutcome<QuireTag> ToTagOutcome(CliResult result)
    {
        if (!result.Succeeded)
        {
            return CliOutcome<QuireTag>.Failure(Describe(result), result);
        }

        if (result.Result is null)
        {
            return CliOutcome<QuireTag>.Failure("CLI 未返回构建结果。", result);
        }

        return CliOutcome<QuireTag>.Success(CliJsonParser.ToTag(result.Result), result);
    }

    private static string Describe(CliResult result) =>
        string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? $"{result.CommandText} 未成功完成，退出码 {result.ExitCode}。"
            : result.ErrorMessage;
}
