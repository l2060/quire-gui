using System.Collections.ObjectModel;
using Avalonia.Media;

namespace QuireStudio.Presentation.Infrastructure;

/// <summary>日志级别，决定图标与颜色。</summary>
public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>一条活动日志。</summary>
public sealed record LogEntry
{
    public required string Message { get; init; }

    public LogLevel Level { get; init; }

    public DateTime Timestamp { get; init; } = DateTime.Now;

    public string TimeText => Timestamp.ToString("HH:mm:ss");

    public string Glyph => Level switch
    {
        LogLevel.Success => "✓",
        LogLevel.Warning => "!",
        LogLevel.Error => "×",
        _ => "·",
    };

    public IBrush Brush => Level switch
    {
        LogLevel.Success => Palette.Success,
        LogLevel.Warning => Palette.Warning,
        LogLevel.Error => Palette.Danger,
        _ => Palette.TextTertiary,
    };
}

/// <summary>
/// 应用级活动日志。命令执行、错误与项目切换都会写入这里，
/// 便于用户在没有终端的情况下复盘 CLI 行为。
/// </summary>
public sealed class ActivityLog
{
    private const int MaxEntries = 300;

    public ObservableCollection<LogEntry> Entries { get; } = [];

    /// <summary>条目变化时触发，用于自动滚动到底部。</summary>
    public event EventHandler? Changed;

    public void Info(string message) => Add(LogLevel.Info, message);

    public void Success(string message) => Add(LogLevel.Success, message);

    public void Warning(string message) => Add(LogLevel.Warning, message);

    public void Error(string message) => Add(LogLevel.Error, message);

    public void Add(LogLevel level, string message)
    {
        if (!UiDispatcher.IsOnUiThread)
        {
            UiDispatcher.Post(() => Add(level, message));
            return;
        }

        Entries.Add(new LogEntry { Level = level, Message = message });
        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(0);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear() => Entries.Clear();
}
