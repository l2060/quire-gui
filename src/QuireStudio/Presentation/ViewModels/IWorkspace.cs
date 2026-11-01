using System.Collections.ObjectModel;
using QuireStudio.Domain;
using QuireStudio.Infrastructure.Cli;
using QuireStudio.Infrastructure.Storage;
using QuireStudio.Presentation.Infrastructure;

namespace QuireStudio.Presentation.ViewModels;

/// <summary>
/// 当前工作区：页面 ViewModel 通过它访问 CLI、项目配置与共享日志，
/// 而不必依赖主窗口实现。
/// </summary>
public interface IWorkspace
{
    QuireCliClient Cli { get; }

    ProjectRegistry Registry { get; }

    AppSettings Settings { get; }

    ActivityLog Log { get; }

    IShellService Shell { get; }

    /// <summary>工作区目录：所有项目与登记表都放在这里；未选择时为空字符串。</summary>
    string WorkspaceDirectory { get; }

    /// <summary>是否已经选择工作区。</summary>
    bool HasWorkspace { get; }

    /// <summary>工作区的显示文本；未选择时给出一句提示。</summary>
    string WorkspaceDisplay { get; }

    /// <summary>工作区里的项目登记表路径。</summary>
    string WorkspaceRegistryPath { get; }

    /// <summary>最近使用的工作区（最多 10 条，最近优先）。</summary>
    ObservableCollection<Components.RecentWorkspaceViewModel> RecentWorkspaces { get; }

    /// <summary>当前激活项目；没有项目时为 <see langword="null"/>。</summary>
    ProjectRecord? Project { get; }

    /// <summary>当前项目解析出的配置；解析失败时为 <see langword="null"/>。</summary>
    ProjectConfiguration? Configuration { get; }

    /// <summary>配置解析失败的原因。</summary>
    string? ConfigurationError { get; }

    /// <summary>保存应用设置，并按新的设置重新探测 CLI。</summary>
    void PersistSettings();

    /// <summary>导航到指定页面。</summary>
    void NavigateTo(string pageKey);

    /// <summary>重新读取当前项目的配置文件。</summary>
    Task ReloadProjectAsync(CancellationToken cancellationToken = default);

    /// <summary>把项目从 GUI 登记表中移除（不删除磁盘文件）。</summary>
    Task RemoveProjectAsync(ProjectRecord project);

    /// <summary>切换工作区：重新载入该目录下的项目登记表。</summary>
    Task ChangeWorkspaceAsync(string directory);

    /// <summary>弹出目录选择框并切换工作区；取消时返回 <see langword="false"/>。</summary>
    Task<bool> ChooseWorkspaceAsync();

    /// <summary>注册或移除资源管理器右键菜单；成功返回 <see langword="null"/>。</summary>
    string? SetShellIntegration(bool enabled);
}
