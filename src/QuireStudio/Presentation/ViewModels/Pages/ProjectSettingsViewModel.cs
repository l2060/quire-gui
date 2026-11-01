using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuireStudio.Domain;
using QuireStudio.Domain.History;
using QuireStudio.Infrastructure;
using QuireStudio.Infrastructure.Cli;
using QuireStudio.Infrastructure.Configuration;
using QuireStudio.Infrastructure.Storage;
using QuireStudio.Presentation.Infrastructure;
using QuireStudio.Presentation.ViewModels.Components;

namespace QuireStudio.Presentation.ViewModels.Pages;

/// <summary>
/// 项目设置页（原「概览」）：左侧是可直接编辑的项目参数与统计，
/// 右侧是完整高度的发布链路。
///
/// <para>
/// 项目名称、输出目录、包后缀、线程数与密钥会写回 YAML；
/// 资源根目录只保存在工作区登记表里，打包/差异时通过 <c>-root</c> 传递，不写入 YAML。
/// </para>
/// </summary>
public sealed partial class ProjectSettingsViewModel : BusyPageViewModel
{
    private const string NoKey = "none";

    private readonly ProjectConfigurationWriter _writer = new();
    private bool _loading;

    public ProjectSettingsViewModel(IWorkspace workspace)
        : base(workspace)
    {
    }

    public override string Key => "project";

    /// <summary>发布链路（全部 Tag，按时间倒序）。</summary>
    public ObservableCollection<TagRowViewModel> GraphRows { get; } = [];

    /// <summary>可选的密钥：<c>none</c> + 本机密钥库中的名字。</summary>
    public ObservableCollection<string> SecretKeyOptions { get; } = [NoKey];

    /// <summary>压缩算法取值（与 CLI 校验一致）。</summary>
    public IReadOnlyList<string> CompressionOptions { get; } = ["lz4", "zlib", "none"];

    /// <summary>压缩策略选项：写回 YAML 的是 <c>always</c> / <c>adaptive</c>，界面显示中文。</summary>
    public IReadOnlyList<ChoiceOption> CompressionPolicyOptions { get; } =
    [
        new("always", "总是压缩"),
        new("adaptive", "自适应（只压缩收益明显的文件）"),
    ];

    /// <summary>当前选中的压缩策略项。</summary>
    [ObservableProperty]
    private ChoiceOption? _selectedCompressionPolicy;

    public ActivityLog Log => Workspace.Log;

    public bool HasTags => GraphRows.Count > 0;

    // ---------------- 统计 ----------------

    [ObservableProperty]
    private string _tagCount = "0";

    [ObservableProperty]
    private string _candidateCount = "0";

    [ObservableProperty]
    private string _packageCount = "0";

    [ObservableProperty]
    private string _baselineName = "—";

    [ObservableProperty]
    private string _baselineHint = "尚未提交任何基线";

    /// <summary>默认压缩算法：none / lz4 / zlib，写入 YAML 的 <c>compression</c>。</summary>
    [ObservableProperty]
    private string _compression = "lz4";

    /// <summary>压缩策略：always / adaptive，写入 YAML 的 <c>compression-policy</c>。</summary>
    [ObservableProperty]
    private string _compressionPolicy = "always";

    [ObservableProperty]
    private string _sourceSummary = "—";

    // ---------------- 表单 ----------------

    [ObservableProperty]
    private string _projectName = "";

    [ObservableProperty]
    private string _outputPath = "";

    [ObservableProperty]
    private string _suffix = "asset";

    [ObservableProperty]
    private string _jobsText = "";

    [ObservableProperty]
    private string _secretKey = NoKey;

    [ObservableProperty]
    private string _resourceRoot = "";

    [ObservableProperty]
    private string _configPath = "";

    [ObservableProperty]
    private string _configDirectory = "";

    [ObservableProperty]
    private string _saveHint = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedChanges))]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isConfirmingRemoval;

    [ObservableProperty]
    private bool _hasConfigurationError;

    [ObservableProperty]
    private string _configurationError = "";

    /// <summary>未保存的编辑。</summary>
    public bool HasUnsavedChanges => IsDirty;

    public bool IsEncryptionEnabled => !ProjectConfiguration.IsDisabledKey(SecretKey);

    public string ResourceRootHint => string.IsNullOrWhiteSpace(ResourceRoot)
        ? "留空：相对资源目录以配置文件所在目录为基准"
        : "仅保存于工作区，打包与差异时通过 -root 传递，不写入 YAML";

    // ---------------- 加载 ----------------

    /// <summary>刷新项目参数、发布链路与密钥选项。</summary>
    [RelayCommand]
    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        ClearError();

        try
        {
            // 顺序很重要：ComboBox 会拒绝 ItemsSource 里不存在的值，
            // 必须先把密钥选项填好，再套用到表单，否则已配置的密钥会被推成 none。
            await LoadSecretKeysAsync(Workspace.Configuration?.SecretKey, cancellationToken).ConfigureAwait(true);
            LoadForm();

            if (Workspace.Project is { } project)
            {
                await LoadTagsAsync(project, cancellationToken).ConfigureAwait(true);
            }
            else
            {
                GraphRows.Clear();
                NotifyGraphChanged();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public override Task ActivateAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync(cancellationToken);

    public override void OnProjectChanged()
    {
        IsConfirmingRemoval = false;
        SaveHint = "";
        ClearError();
        LoadForm();
        GraphRows.Clear();
        NotifyGraphChanged();
    }

    /// <summary>放弃未保存的编辑，重新读取磁盘上的配置。</summary>
    [RelayCommand]
    private async Task ReloadProjectAsync()
    {
        await Workspace.ReloadProjectAsync().ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
        SaveHint = "已重新载入配置文件";
    }

    // ---------------- 保存 ----------------

    /// <summary>把表单写回 YAML（名称/输出/后缀/密钥/线程数）与登记表（资源根目录）。</summary>
    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        if (Workspace.Project is not { } project)
        {
            return;
        }

        ClearError();

        if (!Validate(out string? validationError))
        {
            ReportFailure(validationError, "项目设置不合法。");
            return;
        }

        var update = new ProjectConfigurationUpdate
        {
            ProjectName = ProjectName.Trim(),
            Output = OutputPath.Trim(),
            Suffix = Suffix.Trim(),
            Compression = Compression,
            CompressionPolicy = CompressionPolicy,
            SecretKey = string.IsNullOrWhiteSpace(SecretKey) ? NoKey : SecretKey.Trim(),
            Jobs = ParseJobs(),
        };

        // 项目名变了就要同步 .quire/<project> 目录，否则 Tag 历史会认不出。
        string? previousName = Workspace.Configuration?.ProjectName;
        string newName = ProjectName.Trim();
        bool renaming = !string.IsNullOrWhiteSpace(previousName) &&
                        !string.Equals(previousName.Trim(), newName, StringComparison.OrdinalIgnoreCase);

        if (renaming)
        {
            string? stateError = ProjectStateStore.Rename(project.ConfigPath, previousName, newName);
            if (stateError is not null)
            {
                ReportFailure(stateError, "重命名项目状态目录失败。");
                return;
            }
        }

        string? writeError = _writer.Update(project.ConfigPath, update);
        if (writeError is not null)
        {
            // 配置没写成，把状态目录改回去，避免两边不一致。
            if (renaming)
            {
                ProjectStateStore.Rename(project.ConfigPath, newName, previousName);
            }

            ReportFailure(writeError, "写入配置文件失败。");
            return;
        }

        if (renaming)
        {
            Workspace.Log.Info($"项目状态目录已重命名：.quire/{previousName} → .quire/{newName}");
        }

        ProjectRecord updated = project with
        {
            Name = newName,
            ResourceRoot = NormalizeOptionalPath(ResourceRoot),
        };

        Workspace.Registry.Register(updated);
        if (!Workspace.Registry.Save())
        {
            Workspace.Log.Warning($"无法保存项目登记表：{Workspace.Registry.LastError}");
        }

        await Workspace.ReloadProjectAsync().ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(Workspace.ConfigurationError))
        {
            ReportFailure($"写入后无法解析配置：{Workspace.ConfigurationError}", "写入后无法解析配置。");
            return;
        }

        Workspace.Log.Success($"项目设置已保存：{updated.Name}");
        SaveHint = "已保存到配置文件";
        IsDirty = false;
    }

    // ---------------- 输出目录 ----------------

    /// <summary>手动挑选输出目录；保留 <c>{tag}</c> 占位符，避免多个 Tag 互相覆盖。</summary>
    [RelayCommand]
    private async Task BrowseOutputPathAsync()
    {
        string? start = ProjectPathFormatter.ResolveBaseDirectory(ConfigDirectory, OutputPath);

        if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
        {
            start = Directory.Exists(ConfigDirectory) ? ConfigDirectory : null;
        }

        string? selected = await Workspace.Shell
            .PickFolderAsync("选择输出目录", start)
            .ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        bool keepTag = OutputPath.Contains("{tag}", StringComparison.OrdinalIgnoreCase);
        OutputPath = ProjectPathFormatter.ToOutputTemplate(ConfigDirectory, selected, keepTag);
    }

    // ---------------- 资源目录 ----------------

    [RelayCommand]
    private async Task BrowseResourceRootAsync()
    {
        string? start = !string.IsNullOrWhiteSpace(ResourceRoot) && Directory.Exists(ResourceRoot)
            ? ResourceRoot
            : Workspace.Project?.Directory;

        string? selected = await Workspace.Shell
            .PickFolderAsync("选择资源根目录", start)
            .ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(selected))
        {
            ResourceRoot = selected;
        }
    }

    [RelayCommand]
    private void ClearResourceRoot() => ResourceRoot = "";

    // ---------------- 其它 ----------------

    /// <summary>在资源管理器中定位配置文件（选中该文件，而不是用默认程序打开它）。</summary>
    [RelayCommand]
    private async Task RevealConfigFileAsync()
    {
        string path = ConfigPath;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ReportFailure($"配置文件不存在：{path}", "配置文件不存在。");
            return;
        }

        ClearError();

        if (Workspace.Shell.TryRevealInFileManager(path))
        {
            return;
        }

        await Workspace.Shell.CopyTextAsync(path).ConfigureAwait(true);
        ReportFailure($"无法在文件管理器中定位，路径已复制到剪贴板：{path}", "定位配置文件失败。");
    }

    [RelayCommand]
    private async Task OpenProjectFolderAsync()
    {
        string directory = Workspace.Project?.Directory ?? "";
        if (Directory.Exists(directory))
        {
            await OpenFolderAsync(directory).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void CreateKey() => Workspace.NavigateTo("keys");

    [RelayCommand]
    private void RequestRemove() => IsConfirmingRemoval = true;

    [RelayCommand]
    private void CancelRemove() => IsConfirmingRemoval = false;

    /// <summary>从 GUI 登记表中移除当前项目（不动磁盘文件）。</summary>
    [RelayCommand]
    private async Task ConfirmRemoveAsync()
    {
        if (Workspace.Project is not { } project)
        {
            return;
        }

        IsConfirmingRemoval = false;
        await Workspace.RemoveProjectAsync(project).ConfigureAwait(true);
        Workspace.Log.Warning($"已从 GUI 移除项目 {project.Name}（磁盘文件未删除）。");
    }

    // ---------------- 内部 ----------------

    private void LoadForm()
    {
        _loading = true;

        ProjectRecord? project = Workspace.Project;
        ProjectConfiguration? configuration = Workspace.Configuration;

        ConfigPath = project?.ConfigPath ?? "";
        ConfigDirectory = project?.Directory ?? "";

        ProjectName = configuration?.ProjectName is { Length: > 0 } name
            ? name
            : project?.Name ?? "";

        OutputPath = configuration?.OutputDescription ?? "./dist/{tag}";
        Suffix = configuration?.Suffix ?? "asset";
        JobsText = configuration?.Jobs is > 0
            ? configuration.Jobs.Value.ToString(CultureInfo.InvariantCulture)
            : "";
        SecretKey = string.IsNullOrWhiteSpace(configuration?.SecretKey) ? NoKey : configuration!.SecretKey!;
        ResourceRoot = project?.ResourceRoot ?? "";

        Compression = NormalizeCompression(configuration?.Compression);
        CompressionPolicy = NormalizeCompressionPolicy(configuration?.CompressionPolicy);
        SelectedCompressionPolicy = CompressionPolicyOptions.FirstOrDefault(
            option => string.Equals(option.Value, CompressionPolicy, StringComparison.OrdinalIgnoreCase));
        SourceSummary = configuration is null
            ? "—"
            : $"{configuration.PackageCount} 个资源包 · {configuration.SourceCount} 个源目录";

        HasConfigurationError = !string.IsNullOrWhiteSpace(Workspace.ConfigurationError);
        ConfigurationError = Workspace.ConfigurationError ?? "";
        OnPropertyChanged(nameof(IsEncryptionEnabled));
        OnPropertyChanged(nameof(ResourceRootHint));

        _loading = false;
        IsDirty = false;
    }

    private async Task LoadTagsAsync(ProjectRecord project, CancellationToken cancellationToken)
    {
        CliOutcome<IReadOnlyList<QuireTag>> outcome = await Workspace.Cli
            .GetTagsAsync(project, cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        if (!outcome.Succeeded)
        {
            GraphRows.Clear();
            NotifyGraphChanged();
            ReportFailure(outcome.Error, "读取 Tag 历史失败。");
            return;
        }

        TagGraph graph = TagGraphBuilder.Build(outcome.Value!);

        GraphRows.Clear();
        foreach (TagGraphRow row in graph.Rows)
        {
            GraphRows.Add(new TagRowViewModel(row, graph.LaneCount));
        }

        TagCount = graph.Rows.Count.ToString(CultureInfo.InvariantCulture);
        CandidateCount = graph.Rows.Count(row => row.Tag.IsCandidate).ToString(CultureInfo.InvariantCulture);
        PackageCount = (Workspace.Configuration?.PackageCount ?? 0).ToString(CultureInfo.InvariantCulture);

        TagGraphRow? baseline = graph.Rows.FirstOrDefault(row => row.Tag.IsBaseline);
        if (baseline is not null)
        {
            BaselineName = baseline.Tag.TagName;
            BaselineHint =
                $"{Formatting.RelativeTime(baseline.Tag.BuildTimeLocal)} · {baseline.Tag.TotalFileChanges} 项文件变化";
        }
        else
        {
            BaselineName = "—";
            BaselineHint = "尚未提交任何基线";
        }

        NotifyGraphChanged();
    }

    private async Task LoadSecretKeysAsync(string? configuredKey, CancellationToken cancellationToken)
    {
        CliOutcome<IReadOnlyList<QuireKey>> outcome = await Workspace.Cli
            .GetKeysAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        string selected = string.IsNullOrWhiteSpace(configuredKey) ? NoKey : configuredKey;
        bool wasLoading = _loading;
        _loading = true;

        SecretKeyOptions.Clear();
        SecretKeyOptions.Add(NoKey);

        if (outcome.Succeeded)
        {
            foreach (QuireKey key in outcome.Value!.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!SecretKeyOptions.Contains(key.Name, StringComparer.OrdinalIgnoreCase))
                {
                    SecretKeyOptions.Add(key.Name);
                }
            }
        }
        else
        {
            Workspace.Log.Warning("读取本机密钥库失败，密钥下拉仅显示 none。");
        }

        // 选中的密钥若不在列表里（例如密钥已被删除），仍然保留原值以免误改。
        if (!SecretKeyOptions.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            SecretKeyOptions.Add(selected);
        }

        SecretKey = SecretKeyOptions.FirstOrDefault(
            option => string.Equals(option, selected, StringComparison.OrdinalIgnoreCase)) ?? NoKey;

        _loading = wasLoading;
    }

    private bool Validate(out string? error)
    {
        error = null;

        string name = ProjectName.Trim();
        if (name.Length == 0)
        {
            error = "项目名称不能为空。";
            return false;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
        {
            error = "项目名称不能包含路径分隔符或非法字符。";
            return false;
        }

        string output = OutputPath.Trim();
        if (output.Length == 0)
        {
            error = "输出目录不能为空，例如 ./dist/{tag}";
            return false;
        }

        if (output.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            error = "输出目录包含非法字符。";
            return false;
        }

        string suffix = Suffix.Trim();
        if (suffix.Length == 0)
        {
            error = "包后缀不能为空，通常使用 asset。";
            return false;
        }

        if (suffix.Any(character => !char.IsLetterOrDigit(character) && character is not ('_' or '-')))
        {
            error = "包后缀只能包含字母、数字、下划线与短横线。";
            return false;
        }

        if (!CompressionOptions.Contains(Compression, StringComparer.OrdinalIgnoreCase))
        {
            error = "压缩算法需要是 lz4、zlib 或 none。";
            return false;
        }

        if (!CompressionPolicyOptions.Any(
                option => string.Equals(option.Value, CompressionPolicy, StringComparison.OrdinalIgnoreCase)))
        {
            error = "压缩策略需要是 always 或 adaptive。";
            return false;
        }

        string jobs = JobsText.Trim();
        if (jobs.Length > 0 &&
            (!int.TryParse(jobs, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 0 || parsed > 256))
        {
            error = "线程数需要是 0-256 的整数，留空表示自动。";
            return false;
        }

        string key = SecretKey.Trim();
        if (!ProjectConfiguration.IsDisabledKey(key) &&
            !SecretKeyOptions.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            error = $"本机密钥库里没有名为 {key} 的密钥。";
            return false;
        }

        return true;
    }

    private int? ParseJobs()
    {
        return int.TryParse(JobsText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int jobs) && jobs >= 0
            ? jobs
            : null;
    }

    private static string? NormalizeOptionalPath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : AppPaths.Normalize(path);

    private void MarkDirty()
    {
        if (_loading)
        {
            return;
        }

        IsDirty = true;
        SaveHint = "有未保存的更改";
    }

    private void NotifyGraphChanged() => OnPropertyChanged(nameof(HasTags));

    partial void OnProjectNameChanged(string value) => MarkDirty();

    partial void OnOutputPathChanged(string value) => MarkDirty();

    partial void OnSuffixChanged(string value) => MarkDirty();

    partial void OnCompressionChanged(string value) => MarkDirty();

    partial void OnSelectedCompressionPolicyChanged(ChoiceOption? value)
    {
        if (value is null)
        {
            return;
        }

        CompressionPolicy = value.Value;

        if (!_loading)
        {
            MarkDirty();
        }
    }

    partial void OnCompressionPolicyChanged(string value) => MarkDirty();

    private static string NormalizeCompression(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "none" => "none",
        "zlib" => "zlib",
        _ => "lz4",
    };

    private static string NormalizeCompressionPolicy(string? value) =>
        value?.Trim().ToLowerInvariant() == "adaptive" ? "adaptive" : "always";

    partial void OnJobsTextChanged(string value) => MarkDirty();

    partial void OnResourceRootChanged(string value)
    {
        OnPropertyChanged(nameof(ResourceRootHint));
        MarkDirty();
    }

    partial void OnSecretKeyChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            // ComboBox 清空选择时会推入 null，回落到 none 而不是留空。
            if (!_loading)
            {
                SecretKey = NoKey;
            }

            return;
        }

        OnPropertyChanged(nameof(IsEncryptionEnabled));
        MarkDirty();
    }
}
