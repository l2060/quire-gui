using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuireStudio.Presentation.Infrastructure;
using QuireStudio.Presentation.ViewModels;
using QuireStudio.Presentation.ViewModels.Dialogs;
using QuireStudio.Presentation.Views.Dialogs;

namespace QuireStudio.Presentation.Views;

/// <summary>
/// 主窗口。只承担三件事：自绘窗口镶边、把平台能力注入 ViewModel、首次显示时触发初始化。
/// </summary>
public sealed partial class MainWindow : Window, IShellService
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        InitializeComponent();

        DataContext = viewModel;
        viewModel.AttachShell(this);

        Opened += OnFirstOpened;
    }

    // ------------------------------------------------------------------
    // 窗口镶边
    // ------------------------------------------------------------------

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // 标题栏上的按钮（主题菜单、窗口按钮）自己处理点击；
        // 这里若继续 BeginMoveDrag，弹出层会被拖动操作立刻关掉。
        if (IsInsideInteractiveControl(e.Source as Visual))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximized();
            return;
        }

        BeginMoveDrag(e);
    }

    private static bool IsInsideInteractiveControl(Visual? source)
    {
        for (Visual? current = source; current is not null; current = current.GetVisualParent())
        {
            if (current is Button or ToggleButton or TextBox or ComboBox or ListBox)
            {
                return true;
            }
        }

        return false;
    }

    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ToggleMaximized();

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e) =>
        _viewModel.IsProjectMenuOpen = false;

    private void ToggleMaximized() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private async void OnFirstOpened(object? sender, EventArgs e)
    {
        Opened -= OnFirstOpened;

        try
        {
            await _viewModel.InitializeAsync().ConfigureAwait(true);

            // 导航列表可能在首次布局后回填选中项，等一轮布局再确认启动页。
            Dispatcher.UIThread.Post(_viewModel.AssertInitialPage, DispatcherPriority.Background);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // 初始化失败不应带走整个窗口，把原因写进活动日志供用户排查。
            _viewModel.Log.Error($"初始化失败：{exception.Message}");
        }
    }

    // ------------------------------------------------------------------
    // IShellService
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<string?> PickFileAsync(FilePickerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = request.Title,
                AllowMultiple = false,
                FileTypeFilter = BuildFileTypes(request),
                SuggestedStartLocation = await ResolveFolderAsync(request.StartDirectory).ConfigureAwait(true),
            }).ConfigureAwait(true);

            return files.Count > 0 ? files[0].Path.LocalPath : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> PickSaveFileAsync(FilePickerRequest request, string suggestedFileName)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = request.Title,
                SuggestedFileName = suggestedFileName,
                DefaultExtension = request.Patterns is { Count: > 0 } patterns ? patterns[0] : null,
                FileTypeChoices = BuildFileTypes(request),
                SuggestedStartLocation = await ResolveFolderAsync(request.StartDirectory).ConfigureAwait(true),
            }).ConfigureAwait(true);

            return file?.Path.LocalPath;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(string title, string? startDirectory = null)
    {
        try
        {
            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = await ResolveFolderAsync(startDirectory).ConfigureAwait(true),
            }).ConfigureAwait(true);

            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task CopyTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var clipboard = Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public bool TryOpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool TryRevealInFileManager(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                // /select 让已运行资源管理器在新窗口中定位并选中该文件。
                var startInfo = new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"")
                {
                    UseShellExecute = true,
                };

                Process.Start(startInfo);
                return true;
            }

            return TryOpenInFileManager(Path.GetDirectoryName(filePath) ?? filePath);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            // 退化为打开所在目录，仍比"点了没反应"好。
            return TryOpenInFileManager(Path.GetDirectoryName(filePath) ?? filePath);
        }
    }

    /// <inheritdoc />
    public bool TryOpenInFileManager(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!Directory.Exists(path) && !File.Exists(path)))
        {
            return false;
        }

        try
        {
            // Windows 交给 ShellExecute（由已运行的资源管理器处理），
            // 避免用当前进程的令牌直接启动 explorer.exe——受限环境下会 0xc0000142。
            ProcessStartInfo startInfo = OperatingSystem.IsWindows()
                ? new ProcessStartInfo(path) { UseShellExecute = true }
                : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", $"\"{path}\"")
                {
                    UseShellExecute = false,
                };

            Process.Start(startInfo);
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<NewKeyRequest?> ShowNewKeyDialogAsync(IReadOnlyList<string> existingNames)
    {
        var viewModel = new AddKeyDialogViewModel(existingNames);
        var dialog = new AddKeyDialog(viewModel);

        return await dialog.ShowDialog<NewKeyRequest>(this).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<DiscardCandidateRequest?> ShowDiscardCandidateDialogAsync(string tagName, int outputCount)
    {
        var viewModel = new DiscardCandidateDialogViewModel(tagName, outputCount);
        var dialog = new DiscardCandidateDialog(viewModel);

        return await dialog.ShowDialog<DiscardCandidateRequest>(this).ConfigureAwait(true);
    }

    private async Task<IStorageFolder?> ResolveFolderAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return null;
        }

        try
        {
            return await StorageProvider.TryGetFolderFromPathAsync(path).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static List<FilePickerFileType>? BuildFileTypes(FilePickerRequest request)
    {
        if (request.Patterns is not { Count: > 0 } patterns)
        {
            return null;
        }

        return
        [
            new FilePickerFileType(request.FilterName ?? "文件")
            {
                Patterns = patterns.Select(pattern => pattern.StartsWith('*') ? pattern : $"*.{pattern}").ToArray(),
            },
        ];
    }
}
