using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuireStudio.Infrastructure;
using QuireStudio.Infrastructure.Platform;

namespace QuireStudio.Presentation.ViewModels.Pages;

/// <summary>
/// 全局设置页：quire-cli 位置与系统集成。
///
/// <para>
/// 工作区选择与最近工作区已经移动到左上角的项目菜单里——那里离项目切换更近；
/// 打包参数属于项目设置。本页只保留与具体项目、工作区都无关的选项。
/// </para>
/// </summary>
public sealed partial class GlobalSettingsViewModel : BusyPageViewModel
{
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

    public bool ShellSupported => ExplorerIntegration.IsSupported;

    [ObservableProperty]
    private bool _shellInstalled;

    [ObservableProperty]
    private string _shellStatusText = "";

    [ObservableProperty]
    private string _saveHint = "";

    /// <summary>应用数据目录，仅保存 settings.json。</summary>
    public string AppDataDirectory => AppPaths.Root;

    /// <summary>从设置同步界面状态。</summary>
    [RelayCommand]
    public override Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        CliPath = Workspace.Settings.CliExecutablePath ?? Workspace.Cli.ExecutablePath ?? "";

        RefreshCliStatus();
        RefreshShellState();
        return Task.CompletedTask;
    }

    public override Task ActivateAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync(cancellationToken);

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
