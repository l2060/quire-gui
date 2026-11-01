using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuireStudio.Infrastructure;
using QuireStudio.Infrastructure.Cli;
using QuireStudio.Infrastructure.Platform;

namespace QuireStudio.Presentation.ViewModels.Pages;

/// <summary>
/// 全局设置页：quire-cli 位置、自动下载 / 更新与系统集成。
///
/// <para>
/// 工作区选择与最近工作区已经移动到左上角的项目菜单里——那里离项目切换更近；
/// 打包参数属于项目设置。本页只保留与具体项目、工作区都无关的选项。
/// </para>
/// </summary>
public sealed partial class GlobalSettingsViewModel : BusyPageViewModel
{
    private readonly CliToolInstaller _installer = new();

    public GlobalSettingsViewModel(IWorkspace workspace)
        : base(workspace)
    {
    }

    public override string Key => "global";

    [ObservableProperty]
    private string _cliPath = "";

    [ObservableProperty]
    private bool _cliReady;

    [ObservableProperty]
    private string _cliStatusText = "";

    [ObservableProperty]
    private IBrush _cliStatusBrush = Palette.TextTertiary;

    // ---------------- 下载 / 更新 ----------------

    /// <summary>工具安装目录，展示给用户。</summary>
    public string CliToolDirectory => _installer.ToolPath;

    /// <summary>更新状态说明（已是最新 / 有新版本 / 未安装）。</summary>
    [ObservableProperty]
    private string _cliUpdateText = "尚未检查更新";

    /// <summary>安装按钮的文案，随状态变化。</summary>
    [ObservableProperty]
    private string _installButtonText = "下载安装";

    /// <summary>NuGet 上的最新稳定版本。</summary>
    [ObservableProperty]
    private string? _latestCliVersion;

    [ObservableProperty]
    private bool _isInstallingCli;

    [ObservableProperty]
    private bool _isCheckingCliUpdate;

    [ObservableProperty]
    private double _cliProgressValue;

    [ObservableProperty]
    private string _cliProgressText = "";

    public bool ShellSupported => ExplorerIntegration.IsSupported;

    [ObservableProperty]
    private bool _shellInstalled;

    [ObservableProperty]
    private string _shellStatusText = "";

    [ObservableProperty]
    private string _saveHint = "";

    public bool HasCliUpdate => Workspace.Cli.IsAvailable &&
                                Version.TryParse(Workspace.Cli.Version, out Version? current) &&
                                Version.TryParse(LatestCliVersion, out Version? latest) &&
                                latest > current;

    /// <summary>应用数据目录，仅保存 settings.json。</summary>
    public string AppDataDirectory => AppPaths.Root;

    /// <summary>从设置同步界面状态。</summary>
    [RelayCommand]
    public override Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        CliPath = Workspace.Settings.CliExecutablePath ?? Workspace.Cli.ExecutablePath ?? "";

        RefreshCliStatus();
        RefreshShellState();
        OnPropertyChanged(nameof(HasCliUpdate));
        UpdateInstallButton();
        return Task.CompletedTask;
    }

    public override async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(cancellationToken).ConfigureAwait(true);

        // 打开页面时顺带查一次，用户不用先点"检查更新"也能看到是否过旧。
        await CheckCliUpdatesAsync(cancellationToken).ConfigureAwait(true);
    }

    // ---------------- CLI ----------------

    /// <summary>保存 CLI 路径并重新探测。</summary>
    [RelayCommand]
    private Task RecheckCliAsync()
    {
        ClearError();

        string? normalized = string.IsNullOrWhiteSpace(CliPath) ? null : CliPath.Trim().Trim('"');
        Workspace.Settings.CliExecutablePath = normalized;
        Workspace.PersistSettings();

        CliPath = Workspace.Cli.ExecutablePath ?? normalized ?? "";
        RefreshCliStatus();

        if (Workspace.Cli.IsAvailable)
        {
            SaveHint = "CLI 已就绪";
            Workspace.Log.Success($"已找到 quire-cli {Workspace.Cli.Version}：{Workspace.Cli.ExecutablePath}");
        }
        else
        {
            ReportFailure("未找到 quire-cli，请检查路径或安装 quire-cli 全局工具。", "未找到 quire-cli。");
        }

        return Task.CompletedTask;
    }

    /// <summary>查询 NuGet 上的最新版本并与本机比较。</summary>
    [RelayCommand]
    private async Task CheckCliUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (IsCheckingCliUpdate || IsInstallingCli)
        {
            return;
        }

        IsCheckingCliUpdate = true;
        CliUpdateText = "正在检查更新…";

        try
        {
            (string? version, string? error) = await _installer
                .GetLatestVersionAsync(cancellationToken)
                .ConfigureAwait(true);

            if (version is null)
            {
                CliUpdateText = error ?? "检查更新失败。";
                LatestCliVersion = null;
            }
            else
            {
                LatestCliVersion = version;
                CliUpdateText = DescribeUpdate(version);
            }

            OnPropertyChanged(nameof(HasCliUpdate));
            UpdateInstallButton();
        }
        finally
        {
            IsCheckingCliUpdate = false;
        }
    }

    /// <summary>下载并安装 / 更新 quire-cli 到应用数据目录。</summary>
    [RelayCommand]
    private async Task InstallCliAsync(CancellationToken cancellationToken = default)
    {
        if (IsInstallingCli)
        {
            return;
        }

        ClearError();

        if (LatestCliVersion is null)
        {
            await CheckCliUpdatesAsync(cancellationToken).ConfigureAwait(true);
        }

        string? target = LatestCliVersion;
        if (string.IsNullOrWhiteSpace(target))
        {
            ReportFailure("还没有拿到可安装的版本号，请先检查更新。", "无法确定要安装的版本。");
            return;
        }

        IsInstallingCli = true;
        CliProgressValue = 0;
        CliProgressText = "准备下载…";

        try
        {
            var progress = new Progress<CliInstallProgress>(report =>
            {
                CliProgressValue = report.Percent;
                CliProgressText = report.Message;
            });

            CliInstallResult result = await _installer
                .InstallAsync(target, progress, cancellationToken)
                .ConfigureAwait(true);

            if (!result.Succeeded)
            {
                ReportFailure(result.Error, "安装 quire-cli 失败。");
                CliUpdateText = result.Error ?? "安装失败。";
                return;
            }

            Workspace.Settings.CliExecutablePath = result.ExecutablePath;
            Workspace.PersistSettings();

            Workspace.Cli.PreferredExecutablePath = result.ExecutablePath;
            Workspace.Cli.Refresh();

            CliPath = result.ExecutablePath ?? "";
            RefreshCliStatus();

            SaveHint = $"已安装 quire-cli {result.Version}";
            Workspace.Log.Success($"quire-cli {result.Version} 已安装到 {_installer.ToolPath}");

            await CheckCliUpdatesAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsInstallingCli = false;
            CliProgressText = "";
        }
    }

    private string DescribeUpdate(string latest)
    {
        if (!Workspace.Cli.IsAvailable)
        {
            return $"本机未检测到 quire-cli，可从 NuGet 下载 {latest}。";
        }

        if (Version.TryParse(Workspace.Cli.Version, out Version? current) &&
            Version.TryParse(latest, out Version? latestParsed) &&
            latestParsed > current)
        {
            return $"有新版本 {latest}（当前 {Workspace.Cli.Version}）。";
        }

        return $"已是最新版本 {Workspace.Cli.Version}。";
    }

    private void UpdateInstallButton() => InstallButtonText = !Workspace.Cli.IsAvailable
        ? "下载安装"
        : HasCliUpdate
            ? $"更新到 {LatestCliVersion}"
            : "重新安装";

    // ---------------- 资源管理器集成 ----------------

    [RelayCommand]
    private void InstallShell() => ApplyShellIntegration(true);

    [RelayCommand]
    private void RemoveShell() => ApplyShellIntegration(false);

    private void ApplyShellIntegration(bool enabled)
    {
        ClearError();

        string? error = Workspace.SetShellIntegration(enabled);
        if (error is not null)
        {
            ReportFailure(error, "资源管理器集成失败。");
            return;
        }

        RefreshShellState();
        SaveHint = enabled ? "已注册右键菜单" : "已移除右键菜单";
    }

    // ---------------- 应用数据 ----------------

    [RelayCommand]
    private async Task OpenAppDataFolderAsync()
    {
        AppPaths.EnsureCreated();
        await OpenFolderAsync(AppPaths.Root).ConfigureAwait(true);
    }

    // ---------------- 内部 ----------------

    private void RefreshCliStatus()
    {
        CliReady = Workspace.Cli.IsAvailable;
        CliStatusText = CliReady
            ? $"quire-cli {Workspace.Cli.Version ?? "未知版本"}"
            : "未找到 quire-cli";
        CliStatusBrush = CliReady ? Palette.Success : Palette.Danger;
    }

    private void RefreshShellState()
    {
        ShellInstalled = ExplorerIntegration.IsInstalled;
        ShellStatusText = ShellInstalled ? "已注册" : "未注册";
    }
}
