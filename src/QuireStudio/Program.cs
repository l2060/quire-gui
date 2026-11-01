using Avalonia;
using QuireStudio.Infrastructure;
using QuireStudio.Infrastructure.Platform;

namespace QuireStudio;

/// <summary>应用入口。除了正常启动，还支持注册资源管理器右键菜单。</summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        CommandLineOptions options = CommandLineOptions.Parse(args);

        if (options.RegisterShell || options.UnregisterShell)
        {
            bool register = options.RegisterShell;
            string? error = register
                ? ExplorerIntegration.Install()
                : ExplorerIntegration.Uninstall();

            return error is null ? 0 : 1;
        }

        // --home 必须在任何 AppPaths 访问之前生效。
        if (!string.IsNullOrWhiteSpace(options.Home))
        {
            Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, options.Home);
        }

        InitialPage = options.Page;
        InitialWorkspace = options.Workspace;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    /// <summary>启动时直接打开的页面标识，例如 <c>--page tags</c>。</summary>
    internal static string? InitialPage { get; private set; }

    /// <summary>启动时使用的工作区目录，例如右键菜单传入的 <c>--workspace "D:\game"</c>。</summary>
    internal static string? InitialWorkspace { get; private set; }

    /// <summary>Avalonia 设计器与运行时共用的构建入口。</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
