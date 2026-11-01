using Avalonia;
using Avalonia.Styling;

namespace QuireStudio.Presentation.Infrastructure;

/// <summary>
/// 界面主题的解析与应用。
///
/// <para>
/// 支持三种模式：<c>light</c>、<c>dark</c> 与 <c>system</c>。
/// <c>system</c> 交给 Avalonia 的 <see cref="ThemeVariant.Default"/>，
/// 由操作系统决定；颜色本身来自 <c>Tokens.axaml</c> 的主题字典。
/// </para>
/// </summary>
public static class AppTheme
{
    public const string Light = "light";

    public const string Dark = "dark";

    public const string System = "system";

    /// <summary>把任意输入收敛到三种合法取值之一。</summary>
    public static string Normalize(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        Light => Light,
        Dark => Dark,
        _ => System,
    };

    /// <summary>转换为 Avalonia 的主题变体；<c>system</c> 映射为 <see cref="ThemeVariant.Default"/>。</summary>
    public static ThemeVariant Resolve(string? mode) => Normalize(mode) switch
    {
        Light => ThemeVariant.Light,
        Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    /// <summary>立即应用主题。</summary>
    public static void Apply(string? mode)
    {
        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = Resolve(mode);
        }
    }

    /// <summary>界面显示用的名字。</summary>
    public static string Label(string? mode) => Normalize(mode) switch
    {
        Light => "亮色",
        Dark => "暗色",
        _ => "跟随系统",
    };
}
