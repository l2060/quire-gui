using Avalonia.Threading;

namespace QuireStudio.Presentation.Infrastructure;

/// <summary>把后台回调安全地切换到 UI 线程。</summary>
public static class UiDispatcher
{
    /// <summary>当前线程是否已经是 UI 线程。</summary>
    public static bool IsOnUiThread => Dispatcher.UIThread.CheckAccess();

    public static void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}
