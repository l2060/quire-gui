using Avalonia.Media;

namespace QuireStudio.Presentation;

/// <summary>
/// 与主题令牌一一对应的画刷集合，供 ViewModel 直接返回类型安全的颜色，
/// 避免在 XAML 中堆叠转换器。
/// </summary>
public static class Palette
{
    public static IBrush Accent { get; } = Create("#7C6BFF");

    public static IBrush AccentSoft { get; } = Create("#221E45");

    public static IBrush Cyan { get; } = Create("#35E3CE");

    public static IBrush Success { get; } = Create("#3BD997");

    public static IBrush SuccessSoft { get; } = Create("#0F2E24");

    public static IBrush Warning { get; } = Create("#F5B740");

    public static IBrush WarningSoft { get; } = Create("#332610");

    public static IBrush Danger { get; } = Create("#FF5E7A");

    public static IBrush DangerSoft { get; } = Create("#3A121D");

    public static IBrush Info { get; } = Create("#5AA9FF");

    public static IBrush InfoSoft { get; } = Create("#10233A");

    public static IBrush Text { get; } = Create("#EDF1F9");

    public static IBrush TextSecondary { get; } = Create("#9AA5BA");

    public static IBrush TextTertiary { get; } = Create("#626D82");

    public static IBrush Surface { get; } = Create("#0D1118");

    public static IBrush SurfaceRaised { get; } = Create("#121722");

    public static IBrush SurfaceHover { get; } = Create("#18202D");

    public static IBrush RowActive { get; } = Create("#1A2032");

    public static IBrush Border { get; } = Create("#1C2332");

    public static IBrush Transparent { get; } = new SolidColorBrush(Colors.Transparent);

    private static readonly IBrush[] LaneBrushes =
    [
        Create("#7C6BFF"),
        Create("#35E3CE"),
        Create("#F5B740"),
        Create("#FF5E7A"),
        Create("#5AA9FF"),
        Create("#3BD997"),
        Create("#C084FC"),
        Create("#FB923C"),
    ];

    /// <summary>按泳道索引取得稳定颜色。</summary>
    public static IBrush Lane(int lane) =>
        LaneBrushes[((lane % LaneBrushes.Length) + LaneBrushes.Length) % LaneBrushes.Length];

    /// <summary>按名称派生一个稳定的品牌色，用于项目头像。</summary>
    public static Color AccentFor(string? name)
    {
        int hash = 0;
        foreach (char character in name ?? string.Empty)
        {
            hash = unchecked((hash * 31) + character);
        }

        Color[] palette =
        [
            Color.Parse("#7C6BFF"),
            Color.Parse("#35E3CE"),
            Color.Parse("#F5B740"),
            Color.Parse("#FF5E7A"),
            Color.Parse("#5AA9FF"),
            Color.Parse("#3BD997"),
        ];

        return palette[((hash % palette.Length) + palette.Length) % palette.Length];
    }

    private static IBrush Create(string hex) => new SolidColorBrush(Color.Parse(hex));
}
