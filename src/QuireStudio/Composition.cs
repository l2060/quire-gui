using QuireStudio.Infrastructure;
using QuireStudio.Infrastructure.Cli;
using QuireStudio.Infrastructure.Configuration;
using QuireStudio.Infrastructure.Storage;
using QuireStudio.Presentation.ViewModels;

namespace QuireStudio;

/// <summary>组合根：集中装配服务与 ViewModel，避免在视图里散落依赖构造。</summary>
public static class Composition
{
    public static MainWindowViewModel CreateMainWindowViewModel(
        string? initialPage = null,
        string? initialWorkspace = null)
    {
        // 数据目录不可写时回退到可写位置，绝不让启动失败。
        AppPaths.EnsureInitialized();
        AppPaths.EnsureCreated();

        AppSettings settings = SettingsStore.Load();

        // 工作区来源：启动参数（右键菜单）→ 上次关闭时的记录 → 无。
        // 没有工作区时不擅自创建默认目录，交给界面引导用户选择。
        string? workspace = AppPaths.Normalize(initialWorkspace)
            ?? AppPaths.Normalize(settings.WorkspaceDirectory);

        var configurationReader = new ProjectConfigurationReader();
        ProjectRegistry registry = ProjectRegistry.CreateEmpty();

        if (workspace is not null)
        {
            AppPaths.EnsureWorkspace(workspace);
            registry = ProjectRegistry.Load(AppPaths.RegistryFile(workspace));

            // 空工作区自动识别根目录下已有的 quire 配置，右键打开工程目录即可直接使用。
            if (registry.Count == 0)
            {
                WorkspaceProjectImporter.Import(workspace, registry, configurationReader);
            }

            if (registry.ActiveProject is null && registry.Projects.Count > 0)
            {
                registry.SetActive(registry.Projects[0].Id);
                registry.Save();
            }
        }

        var cli = new QuireCliClient
        {
            PreferredExecutablePath = settings.CliExecutablePath,
        };
        cli.Refresh();

        return new MainWindowViewModel(
            cli,
            registry,
            settings,
            workspace,
            configurationReader,
            initialPage,
            AppPaths.StartupWarning);
    }
}
