using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuireStudio.Domain;
using QuireStudio.Infrastructure;
using QuireStudio.Infrastructure.Cli;
using QuireStudio.Infrastructure.Configuration;
using QuireStudio.Infrastructure.Platform;
using QuireStudio.Infrastructure.Storage;
using QuireStudio.Presentation.Infrastructure;
using QuireStudio.Presentation.ViewModels.Components;
using QuireStudio.Presentation.ViewModels.Pages;

namespace QuireStudio.Presentation.ViewModels;

/// <summary>
/// 主窗口 ViewModel：持有 GUI 统一的项目登记表、当前项目上下文与各页面 ViewModel，
/// 并作为 <see cref="IWorkspace"/> 对页面暴露服务。
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IWorkspace
{
    private readonly ProjectConfigurationReader _configurationReader;
    private readonly string? _startupNotice;
    private readonly string? _initialPage;
    private IShellService _shell = NullShellService.Instance;
    private ProjectRegistry _registry;
    private bool _isReady;

    public MainWindowViewModel(
        QuireCliClient cli,
        ProjectRegistry registry,
        AppSettings settings,
        string? workspaceDirectory,
        ProjectConfigurationReader? configurationReader = null,
        string? initialPage = null,
        string? startupNotice = null)
    {
        ArgumentNullException.ThrowIfNull(cli);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settings);

        Cli = cli;
        _registry = registry;
        Settings = settings;
        _startupNotice = startupNotice;
        _initialPage = initialPage;
        WorkspaceDirectory = workspaceDirectory ?? "";
        _configurationReader = configurationReader ?? new ProjectConfigurationReader();
        Log = new ActivityLog();

        // 主题必须在窗口显示前应用，避免先闪一下默认配色。
        ThemeMode = AppTheme.Normalize(settings.ThemeMode);
        AppTheme.Apply(ThemeMode);

        Cli.PreferredExecutablePath = settings.CliExecutablePath;
        Cli.Refresh();

        ProjectSettings = new ProjectSettingsViewModel(this);
        Tags = new TagsViewModel(this);
        Changes = new ChangesViewModel(this);
        Keys = new KeysViewModel(this);
        LogPage = new LogViewModel(this);
        GlobalSettings = new GlobalSettingsViewModel(this);

        NavItems =
        [
            new NavItemViewModel("project", "项目设置", Icons.Sliders, ProjectSettings),
            new NavItemViewModel("tags", "打包分支", Icons.Branch, Tags),
            new NavItemViewModel("changes", "基线变更", Icons.Diff, Changes),
            new NavItemViewModel("log", "运行日志", Icons.History, LogPage),
            new NavItemViewModel("keys", "密钥库", Icons.Key, Keys),
            new NavItemViewModel("global", "全局设置", Icons.Globe, GlobalSettings),
        ];

        Tags.PropertyChanged += OnTagsPropertyChanged;

        UpdateCliStatus();
        RefreshProjects();
        UpdateTitles();
        RefreshRecentWorkspaces();

        if (HasWorkspace)
        {
            RememberWorkspace(WorkspaceDirectory);
        }

        SelectedNav = NavItems[0];

        if (!string.IsNullOrWhiteSpace(initialPage))
        {
            NavigateTo(initialPage);
        }

        if (!string.IsNullOrWhiteSpace(_startupNotice))
        {
            AppNotice = _startupNotice;
            Log.Warning(_startupNotice);
        }
    }

    // ------------------------------------------------------------------
    // IWorkspace
    // ------------------------------------------------------------------

    public QuireCliClient Cli { get; }

    public ProjectRegistry Registry => _registry;

    public AppSettings Settings { get; }

    public ActivityLog Log { get; }

    public IShellService Shell => _shell;

    /// <summary>工作区目录：所有项目与登记表都放在这里；未选择时为空字符串。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkspaceRegistryPath))]
    [NotifyPropertyChangedFor(nameof(HasWorkspace))]
    [NotifyPropertyChangedFor(nameof(WorkspaceDisplay))]
    [NotifyPropertyChangedFor(nameof(WorkspaceTitle))]
    [NotifyCanExecuteChangedFor(nameof(CreateProjectCommand))]
    private string _workspaceDirectory;

    /// <summary>是否已经选择工作区。</summary>
    public bool HasWorkspace => !string.IsNullOrWhiteSpace(WorkspaceDirectory);

    /// <summary>用于标题栏与提示的工作区显示值。</summary>
    public string WorkspaceDisplay => HasWorkspace ? WorkspaceDirectory : "尚未选择工作区";

    /// <summary>应用数据目录，只保存 <c>settings.json</c>。</summary>
    public string AppDataDirectory => AppPaths.Root;

    /// <summary>工作区里的项目登记表路径；未选择工作区时为空。</summary>
    public string WorkspaceRegistryPath =>
        HasWorkspace ? AppPaths.RegistryFile(WorkspaceDirectory) : "";

    public ProjectSettingsViewModel ProjectSettings { get; }

    public TagsViewModel Tags { get; }

    public ChangesViewModel Changes { get; }

    public KeysViewModel Keys { get; }

    public LogViewModel LogPage { get; }

    public GlobalSettingsViewModel GlobalSettings { get; }

    public ObservableCollection<NavItemViewModel> NavItems { get; } = [];

    /// <summary>项目切换列表。</summary>
    public ObservableCollection<ProjectOptionViewModel> Projects { get; } = [];

    /// <summary>最近使用的工作区，最多 <see cref="AppSettings.MaxRecentWorkspaces"/> 条。</summary>
    public ObservableCollection<RecentWorkspaceViewModel> RecentWorkspaces { get; } = [];

    [ObservableProperty]
    private bool _hasRecentWorkspaces;

    [ObservableProperty]
    private ProjectRecord? _project;

    [ObservableProperty]
    private ProjectConfiguration? _configuration;

    [ObservableProperty]
    private string? _configurationError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnboarding))]
    private bool _hasProject;

    public bool IsOnboarding => !HasProject;

    [ObservableProperty]
    private string _projectTitle = "未选择项目";

    /// <summary>标题栏显示的工作区目录（过长时保留尾部并省略开头）。</summary>
    [ObservableProperty]
    private string _workspaceTitle = "未选择工作区";

    /// <summary>标题栏显示的当前项目配置文件名。</summary>
    [ObservableProperty]
    private string _configFileTitle = "";

    [ObservableProperty]
    private bool _hasConfigFile;

    [ObservableProperty]
    private string _projectInitial = "Q";

    [ObservableProperty]
    private string _projectConfigFileName = "未登记项目";

    [ObservableProperty]
    private IBrush _projectAccent = new SolidColorBrush(Palette.AccentFor("Quire"));

    [ObservableProperty]
    private string _projectFilePath = "";

    [ObservableProperty]
    private string _windowTitle = "Quire Studio";

    /// <summary>界面主题：<c>system</c> / <c>light</c> / <c>dark</c>。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeLabel))]
    [NotifyPropertyChangedFor(nameof(ThemeIcon))]
    [NotifyPropertyChangedFor(nameof(IsThemeLight))]
    [NotifyPropertyChangedFor(nameof(IsThemeDark))]
    [NotifyPropertyChangedFor(nameof(IsThemeSystem))]
    private string _themeMode = AppTheme.System;

    /// <summary>当前主题的显示名。</summary>
    public string ThemeLabel => AppTheme.Label(ThemeMode);

    /// <summary>标题栏按钮上的主题图标。</summary>
    public Geometry ThemeIcon => AppTheme.Normalize(ThemeMode) switch
    {
        AppTheme.Light => Icons.Sun,
        AppTheme.Dark => Icons.Moon,
        _ => Icons.Monitor,
    };

    public bool IsThemeLight => AppTheme.Normalize(ThemeMode) == AppTheme.Light;

    public bool IsThemeDark => AppTheme.Normalize(ThemeMode) == AppTheme.Dark;

    public bool IsThemeSystem => AppTheme.Normalize(ThemeMode) == AppTheme.System;

    /// <summary>本程序（Quire Studio）的仓库地址。</summary>
    public string StudioRepositoryUrl => AppLinks.Studio;

    /// <summary>quire-cli 的仓库地址。</summary>
    public string QuireRepositoryUrl => AppLinks.Quire;

    /// <summary>用默认浏览器打开链接。</summary>
    [RelayCommand]
    private void OpenLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (!Shell.TryOpenUrl(url))
        {
            Log.Warning($"无法在浏览器中打开链接：{url}");
        }
    }

    /// <summary>切换主题并写入设置，下次启动沿用。</summary>
    [RelayCommand]
    private void SetTheme(string? mode)
    {
        string normalized = AppTheme.Normalize(mode);
        IsThemeMenuOpen = false;

        if (AppTheme.Normalize(ThemeMode) == normalized)
        {
            return;
        }

        ThemeMode = normalized;
        AppTheme.Apply(normalized);

        Settings.ThemeMode = normalized;
        PersistSettings();
        Log.Info($"界面主题已切换为{AppTheme.Label(normalized)}。");
    }

    /// <summary>主题菜单是否展开。</summary>
    [ObservableProperty]
    private bool _isThemeMenuOpen;

    [RelayCommand]
    private void ToggleThemeMenu() => IsThemeMenuOpen = !IsThemeMenuOpen;

    [RelayCommand]
    private void CloseThemeMenu() => IsThemeMenuOpen = false;

    [ObservableProperty]
    private NavItemViewModel? _selectedNav;

    [ObservableProperty]
    private PageViewModel? _currentPage;

    [ObservableProperty]
    private bool _cliReady;

    [ObservableProperty]
    private string _cliStatusText = "";

    [ObservableProperty]
    private string _cliPathText = "";

    [ObservableProperty]
    private IBrush _cliStatusBrush = Palette.Warning;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isProjectMenuOpen;

    [ObservableProperty]
    private string _newProjectName = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateProjectCommand))]
    private bool _isCreatingProject;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProjectError))]
    private string? _projectError;

    public bool HasProjectError => !string.IsNullOrWhiteSpace(ProjectError);

    /// <summary>初始化：打开上次使用的项目，并激活首页。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Registry.ActiveProject is { } active)
        {
            await OpenProjectAsync(active, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            HasProject = false;
            UpdateTitles();
        }

        _isReady = true;
        AssertInitialPage();

        if (CurrentPage is { } page)
        {
            await page.ActivateAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 重新断言启动页。
    ///
    /// <para>
    /// 导航列表绑定 <c>SelectedItem</c> 时会先把选中项回填成第一项，
    /// 因此 <c>--page</c> 需要在窗口完成第一次布局后再确认一次，否则会被覆盖。
    /// </para>
    /// </summary>
    public void AssertInitialPage()
    {
        if (!string.IsNullOrWhiteSpace(_initialPage) && CurrentPage?.Key != _initialPage)
        {
            NavigateTo(_initialPage);
        }
    }

    /// <inheritdoc />
    public void PersistSettings()
    {
        if (!SettingsStore.Save(Settings, out string? error))
        {
            AppNotice = $"无法保存设置到 {AppPaths.SettingsFile}（{error}）。";
            Log.Warning(AppNotice);
        }

        Cli.PreferredExecutablePath = Settings.CliExecutablePath;
        Cli.Refresh();
        UpdateCliStatus();
        Log.Info("应用设置已保存。");
    }

    /// <summary>记录当前工作区到设置与历史列表。</summary>
    private void RememberWorkspace(string workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace))
        {
            return;
        }

        Settings.WorkspaceDirectory = workspace;

        List<string> recent = Settings.RecentWorkspaces;
        recent.RemoveAll(path => string.Equals(path, workspace, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, workspace);

        while (recent.Count > AppSettings.MaxRecentWorkspaces)
        {
            recent.RemoveAt(recent.Count - 1);
        }

        if (SettingsStore.Save(Settings, out string? error))
        {
            // 启动时的数据目录告警需要一直可见，直到用户主动关闭。
            AppNotice = _startupNotice;
        }
        else
        {
            // 设置写不进去通常意味着当前会话权限受限；必须让用户看到，而不是默默不记住。
            AppNotice =
                $"无法保存设置到 {AppPaths.SettingsFile}（{error}）。当前会话仍可使用，但重启后工作区与项目不会保留。";
            Log.Warning(AppNotice);
        }

        RefreshRecentWorkspaces();
    }

    /// <inheritdoc />
    public async Task<bool> ChooseWorkspaceAsync()
    {
        string? selected = await Shell
            .PickFolderAsync("选择工作区目录", RecentWorkspaces.FirstOrDefault()?.Path)
            .ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(selected))
        {
            return false;
        }

        await ChangeWorkspaceAsync(selected).ConfigureAwait(true);
        return HasWorkspace;
    }

    private void RefreshRecentWorkspaces()
    {
        RecentWorkspaces.Clear();

        foreach (string path in Settings.RecentWorkspaces)
        {
            bool isCurrent = string.Equals(path, WorkspaceDirectory, StringComparison.OrdinalIgnoreCase);
            RecentWorkspaces.Add(new RecentWorkspaceViewModel(
                path,
                isCurrent,
                new AsyncRelayCommand(() => ChangeWorkspaceAsync(path))));
        }

        HasRecentWorkspaces = RecentWorkspaces.Count > 0;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAppNotice))]
    private string? _appNotice;

    /// <summary>是否存在需要常驻提示的环境问题（例如设置无法写入）。</summary>
    public bool HasAppNotice => !string.IsNullOrWhiteSpace(AppNotice);

    /// <summary>关闭常驻提示。</summary>
    [RelayCommand]
    private void DismissAppNotice() => AppNotice = null;

    /// <summary>复制常驻提示文本。</summary>
    [RelayCommand]
    private async Task CopyAppNoticeAsync()
    {
        if (!string.IsNullOrWhiteSpace(AppNotice))
        {
            await Shell.CopyTextAsync(AppNotice).ConfigureAwait(true);
        }
    }

    /// <summary>保存登记表，失败时写入活动日志。</summary>
    private void SaveRegistry()
    {
        if (!Registry.Save())
        {
            Log.Warning($"无法保存项目登记表：{Registry.LastError}");
        }
    }

    /// <inheritdoc />
    public void NavigateTo(string pageKey)
    {
        // 兼容旧参数：概览与原「项目设置」页已合并为「项目设置」。
        pageKey = pageKey switch
        {
            "overview" or "settings" => "project",
            _ => pageKey,
        };

        NavItemViewModel? item = NavItems.FirstOrDefault(nav => nav.Key == pageKey);
        if (item is null)
        {
            return;
        }

        if (ReferenceEquals(SelectedNav, item))
        {
            _ = item.Page.ActivateAsync();
            return;
        }

        SelectedNav = item;
    }

    /// <inheritdoc />
    public Task ReloadProjectAsync(CancellationToken cancellationToken = default)
    {
        if (Project is { } current && Registry.Find(current.Id) is { } latest)
        {
            Project = latest;
        }

        LoadConfiguration();
        UpdateTitles();
        RefreshProjects();
        NotifyPagesProjectChanged();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RemoveProjectAsync(ProjectRecord project)
    {
        ArgumentNullException.ThrowIfNull(project);

        bool wasActive = project.Id == Project?.Id;
        Registry.Remove(project.Id);
        SaveRegistry();

        if (!wasActive)
        {
            RefreshProjects();
            return;
        }

        Project = null;
        Configuration = null;
        ConfigurationError = null;
        HasProject = false;
        NotifyPagesProjectChanged();
        RefreshProjects();
        UpdateTitles();

        if (Registry.ActiveProject is { } next)
        {
            await OpenProjectAsync(next).ConfigureAwait(true);
        }
    }

    // ------------------------------------------------------------------
    // 命令
    // ------------------------------------------------------------------

    /// <summary>强制刷新当前页面。</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (CurrentPage is not { } page)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await page.RefreshAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>在工作区内创建新项目。</summary>
    [RelayCommand(CanExecute = nameof(CanCreateProject))]
    private async Task CreateProjectAsync()
    {
        if (!HasWorkspace)
        {
            ProjectError = "请先选择工作区，再在工作区内创建项目。";
            return;
        }

        string name = NewProjectName.Trim();
        if (!IsValidProjectName(name))
        {
            ProjectError = "项目名不能为空，且不能包含路径分隔符、花括号或文件名非法字符。";
            return;
        }

        ProjectError = null;
        IsCreatingProject = true;

        try
        {
            AppPaths.EnsureWorkspace(WorkspaceDirectory);
            string directory = Path.Combine(WorkspaceDirectory, name);
            Directory.CreateDirectory(directory);

            Log.Info($"初始化项目 {name}：{directory}");
            CliOutcome<string> outcome = await Cli
                .InitProjectAsync(name, directory)
                .ConfigureAwait(true);

            if (!outcome.Succeeded)
            {
                ProjectError = outcome.Error;
                Log.Error(outcome.Error ?? "初始化项目失败。");
                return;
            }

            string configPath = outcome.Value!;
            ProjectRecord record = Registry.Register(new ProjectRecord
            {
                Name = name,
                ConfigPath = configPath,
                IsManaged = true,
            });
            SaveRegistry();

            NewProjectName = "";
            await OpenProjectAsync(record).ConfigureAwait(true);
            Log.Success($"项目 {name} 已创建：{configPath}");
        }
        finally
        {
            IsCreatingProject = false;
        }
    }

    /// <summary>把磁盘上已有的 quire 配置登记到 GUI。</summary>
    [RelayCommand]
    private async Task AddExistingProjectAsync()
    {
        string? selected = await Shell
            .PickFileAsync(new FilePickerRequest(
                "选择 quire 配置文件",
                Directory.Exists(WorkspaceDirectory) ? WorkspaceDirectory : null,
                "Quire 配置",
                ["yml", "yaml"]))
            .ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        ProjectConfigurationLoadResult result = _configurationReader.Read(selected);
        string name = result.Configuration?.DisplayName ?? Path.GetFileNameWithoutExtension(selected);

        string configPath = Path.GetFullPath(selected);

        // 工作区外的项目：整目录复制进工作区（YAML + .quire + 相对资源目录，跳过 dist）。
        if (!IsManagedPath(configPath))
        {
            IsCreatingProject = true;

            try
            {
                Log.Info($"正在导入项目到工作区：{name}");

                ProjectImportResult import = await Task
                    .Run(() => ProjectImportService.Import(selected, WorkspaceDirectory, result.Configuration))
                    .ConfigureAwait(true);

                if (!import.Succeeded)
                {
                    ProjectError = import.Error;
                    Log.Error(import.Error ?? "导入项目失败。");
                    return;
                }

                configPath = import.ConfigPath!;
                Log.Success($"项目 {name} 已导入工作区：复制 {import.CopiedFiles} 个文件 → {Path.GetDirectoryName(configPath)}");

                if (import.ExternalSources.Count > 0)
                {
                    Log.Warning(
                        $"以下资源目录位于项目之外，未随项目复制，仍指向原位置：{string.Join("、", import.ExternalSources)}");
                }
            }
            finally
            {
                IsCreatingProject = false;
            }
        }

        ProjectRecord record = Registry.Register(new ProjectRecord
        {
            Name = name,
            ConfigPath = configPath,
            IsManaged = true,
        });
        SaveRegistry();

        ProjectError = result.Succeeded ? null : result.Error;
        await OpenProjectAsync(record).ConfigureAwait(true);
    }

    /// <summary>打开工作区目录。</summary>
    [RelayCommand]
    private async Task OpenWorkspaceFolderAsync()
    {
        if (!HasWorkspace)
        {
            return;
        }

        AppPaths.EnsureWorkspace(WorkspaceDirectory);

        if (Shell.TryOpenInFileManager(WorkspaceDirectory))
        {
            return;
        }

        await Shell.CopyTextAsync(WorkspaceDirectory).ConfigureAwait(true);
        AppNotice = $"系统无法打开文件管理器，工作区路径已复制到剪贴板：{WorkspaceDirectory}";
    }

    /// <summary>打开应用数据目录（仅保存 settings.json）。</summary>
    [RelayCommand]
    private async Task OpenAppDataFolderAsync()
    {
        AppPaths.EnsureCreated();

        if (Shell.TryOpenInFileManager(AppPaths.Root))
        {
            return;
        }

        await Shell.CopyTextAsync(AppPaths.Root).ConfigureAwait(true);
        AppNotice = $"系统无法打开文件管理器，目录路径已复制到剪贴板：{AppPaths.Root}";
    }

    /// <summary>跳转到项目设置页。</summary>
    [RelayCommand]
    private void OpenSettings() => NavigateTo("settings");

    /// <summary>切换项目选择面板。</summary>
    [RelayCommand]
    private void ToggleProjectMenu() => IsProjectMenuOpen = !IsProjectMenuOpen;

    /// <summary>关闭项目选择面板。</summary>
    [RelayCommand]
    private void CloseProjectMenu() => IsProjectMenuOpen = false;

    /// <summary>选择工作区（未选择时的唯一入口）。</summary>
    [RelayCommand]
    private async Task ChooseWorkspace()
    {
        await ChooseWorkspaceAsync().ConfigureAwait(true);
        IsProjectMenuOpen = false;
    }

    private bool CanCreateProject => !IsCreatingProject && HasWorkspace;

    /// <inheritdoc />
    public async Task ChangeWorkspaceAsync(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        if (AppPaths.Normalize(directory) is not { } workspace)
        {
            Log.Error($"工作区路径无效：{directory}");
            return;
        }

        AppPaths.EnsureWorkspace(workspace);

        WorkspaceDirectory = workspace;
        RememberWorkspace(workspace);
        _registry = ProjectRegistry.Load(WorkspaceRegistryPath);

        if (_registry.Count == 0)
        {
            int imported = WorkspaceProjectImporter.Import(workspace, _registry, _configurationReader);
            if (imported > 0)
            {
                Log.Info($"已在工作区中识别 {imported} 个现有配置。");
            }
        }

        Project = null;
        Configuration = null;
        ConfigurationError = null;
        HasProject = false;
        NotifyPagesProjectChanged();
        RefreshProjects();
        UpdateTitles();
        OnPropertyChanged(nameof(WorkspaceRegistryPath));

        if (Registry.ActiveProject is { } active)
        {
            await OpenProjectAsync(active).ConfigureAwait(true);
        }

        Log.Info($"工作区已切换到 {workspace}（{Registry.Count} 个项目）");
    }

    /// <inheritdoc />
    public string? SetShellIntegration(bool enabled)
    {
        string? error = enabled
            ? ExplorerIntegration.Install()
            : ExplorerIntegration.Uninstall();

        if (error is null)
        {
            Log.Success(enabled
                ? "已注册资源管理器右键菜单：右键文件夹即可作为工作区打开。"
                : "已移除资源管理器右键菜单。");
        }
        else
        {
            Log.Error($"资源管理器集成失败：{error}");
        }

        return error;
    }

    // ------------------------------------------------------------------
    // 内部流程
    // ------------------------------------------------------------------

    private async Task OpenProjectAsync(ProjectRecord record, CancellationToken cancellationToken = default)
    {
        ProjectRecord opened = Registry.Register(record);
        Registry.SetActive(opened.Id);
        SaveRegistry();

        Project = opened;
        LoadConfiguration();
        HasProject = true;
        IsProjectMenuOpen = false;
        UpdateTitles();
        RefreshProjects();
        NotifyPagesProjectChanged();
        UpdateNavBadges();

        if (_isReady && CurrentPage is { } page)
        {
            await page.ActivateAsync(cancellationToken).ConfigureAwait(true);
        }

        Log.Info($"已切换到项目 {opened.Name}");
    }

    private void LoadConfiguration()
    {
        if (Project is null)
        {
            Configuration = null;
            ConfigurationError = null;
            return;
        }

        ProjectConfigurationLoadResult result = _configurationReader.Read(Project.ConfigPath);
        Configuration = result.Configuration;
        ConfigurationError = result.Error;

        if (!result.Succeeded)
        {
            Log.Error($"读取配置失败：{result.Error}");
        }
    }

    private void NotifyPagesProjectChanged()
    {
        ProjectSettings.OnProjectChanged();
        Tags.OnProjectChanged();
        Changes.OnProjectChanged();
        Keys.OnProjectChanged();
        LogPage.OnProjectChanged();
        GlobalSettings.OnProjectChanged();
    }

    private void RefreshProjects()
    {
        Projects.Clear();
        string? activeId = Project?.Id;

        foreach (ProjectRecord record in Registry.Projects.OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase))
        {
            var option = new ProjectOptionViewModel(
                record,
                record.Id == activeId,
                new AsyncRelayCommand(() => SwitchProjectAsync(record)));
            Projects.Add(option);
        }
    }

    private async Task SwitchProjectAsync(ProjectRecord record)
    {
        if (record.Id == Project?.Id)
        {
            return;
        }

        if (Registry.Find(record.Id) is null)
        {
            return;
        }

        await OpenProjectAsync(record).ConfigureAwait(true);
    }

    private void UpdateTitles()
    {
        ProjectTitle = Configuration?.DisplayName
            ?? (string.IsNullOrWhiteSpace(Project?.Name) ? "未选择项目" : Project!.Name);
        ProjectFilePath = Project?.ConfigPath ?? "";
        WindowTitle = HasProject ? $"Quire Studio — {ProjectTitle}" : "Quire Studio";

        WorkspaceTitle = HasWorkspace ? AbbreviatePath(WorkspaceDirectory) : "未选择工作区";
        ConfigFileTitle = Project?.ConfigFileName ?? "";
        HasConfigFile = !string.IsNullOrWhiteSpace(ConfigFileTitle);

        ProjectInitial = string.IsNullOrEmpty(ProjectTitle) ? "Q" : ProjectTitle[..1].ToUpperInvariant();
        ProjectConfigFileName = Project?.ConfigFileName ?? "未登记项目";
        ProjectAccent = new SolidColorBrush(Palette.AccentFor(ProjectTitle));
    }

    /// <summary>路径过长时保留尾部，便于一眼看出目录名。</summary>
    private static string AbbreviatePath(string path, int maxLength = 46)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= maxLength)
        {
            return path;
        }

        return "…" + path[^(maxLength - 1)..];
    }

    private void UpdateCliStatus()
    {
        CliReady = Cli.IsAvailable;
        CliStatusText = CliReady
            ? $"quire-cli {Cli.Version}".Trim()
            : "找不到 quire-cli";
        CliPathText = Cli.ExecutablePath ?? "未检测到可执行文件";
        CliStatusBrush = CliReady ? Palette.Success : Palette.Danger;
    }

    private void UpdateNavBadges()
    {
        NavItemViewModel? tagsNav = NavItems.FirstOrDefault(nav => nav.Key == "tags");
        if (tagsNav is null)
        {
            return;
        }

        tagsNav.Badge = Tags.CandidateCount > 0
            ? Tags.CandidateCount.ToString()
            : null;
    }

    private void OnTagsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TagsViewModel.CandidateCount))
        {
            UpdateNavBadges();
        }
    }

    partial void OnSelectedNavChanged(NavItemViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        foreach (NavItemViewModel item in NavItems)
        {
            item.IsActive = ReferenceEquals(item, value);
        }

        CurrentPage = value.Page;
    }

    partial void OnCurrentPageChanged(PageViewModel? value)
    {
        if (!_isReady || value is null)
        {
            return;
        }

        _ = value.ActivateAsync();
    }

    /// <summary>挂载宿主窗口提供的平台能力。</summary>
    public void AttachShell(IShellService shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
    }

    private static bool IsValidProjectName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name.StartsWith('.') || name.StartsWith('-'))
        {
            return false;
        }

        return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
               name.IndexOfAny(['/', '\\', '{', '}']) < 0;
    }

    private bool IsManagedPath(string path)
    {
        try
        {
            string root = Path.GetFullPath(WorkspaceDirectory);
            string full = Path.GetFullPath(path);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
