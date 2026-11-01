using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using QuireStudio.Presentation.ViewModels;
using QuireStudio.Presentation.Views;

namespace QuireStudio;

/// <summary>应用根对象，负责装配依赖并创建主窗口。</summary>
public sealed partial class App : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindowViewModel viewModel = Composition.CreateMainWindowViewModel(
                Program.InitialPage,
                Program.InitialWorkspace);
            desktop.MainWindow = new MainWindow(viewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
