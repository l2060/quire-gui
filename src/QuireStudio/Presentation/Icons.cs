using Avalonia.Media;

namespace QuireStudio.Presentation;

/// <summary>
/// 界面使用的线性图标（24×24 视图框）。统一用描边绘制，
/// 由 <see cref="Controls.IconPresenter"/> 或 Viewbox 缩放到目标尺寸。
/// </summary>
public static class Icons
{
    public static Geometry Dashboard { get; } = Parse("M3 4h7v7H3zM14 4h7v4h-7zM14 11h7v9h-7zM3 14h7v6H3z");

    public static Geometry Branch { get; } = Parse(
        "M6 4v10M6 9h5a5 5 0 0 0 5-5M16 11v2.5" +
        "M6 19.5a2.5 2.5 0 1 1 0-5 2.5 2.5 0 0 1 0 5z" +
        "M16 20a2.5 2.5 0 1 1 0-5 2.5 2.5 0 0 1 0 5z");

    public static Geometry History { get; } = Parse(
        "M12 7.5v5l3.2 1.9M21 12a9 9 0 1 1-2.7-6.4M20.6 3.6v5h-5");

    public static Geometry Key { get; } = Parse(
        "M9 15.5a4.5 4.5 0 1 1 0-9 4.5 4.5 0 0 1 0 9zM12.2 11.2L21 2.4M17.2 6.2l2.2 2.2");

    public static Geometry Sliders { get; } = Parse(
        "M4 7h7M15 7h5M4 12h4M12 12h8M4 17h9M18 17h2" +
        "M13 9a2 2 0 1 0 0-4 2 2 0 0 0 0 4z" +
        "M10 14a2 2 0 1 0 0-4 2 2 0 0 0 0 4z" +
        "M15 19a2 2 0 1 0 0-4 2 2 0 0 0 0 4z");

    public static Geometry Package { get; } = Parse(
        "M12 3l8.5 4.7v8.6L12 21l-8.5-4.7V7.7zM3.5 7.7L12 12.4l8.5-4.7M12 12.4V21");

    public static Geometry Refresh { get; } = Parse(
        "M20 11.5a8 8 0 0 0-13.7-5.6L3.5 8.5M3.5 4v4.5H8" +
        "M4 12.5a8 8 0 0 0 13.7 5.6L20.5 15.5M20.5 20v-4.5H16");

    public static Geometry Plus { get; } = Parse("M12 5v14M5 12h14");

    public static Geometry Minus { get; } = Parse("M5 12h14");

    public static Geometry Trash { get; } = Parse(
        "M4 7h16M9.5 7V4.5h5V7M6.5 7l1 13.5h9l1-13.5M10.5 11v6M13.5 11v6");

    public static Geometry Folder { get; } = Parse(
        "M3 18.5V6.5A1.5 1.5 0 0 1 4.5 5h4.6l2 2.5h8.4A1.5 1.5 0 0 1 21 9v9.5" +
        "a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18.5z");

    public static Geometry ChevronDown { get; } = Parse("M6.5 9.5l5.5 5.5 5.5-5.5");

    public static Geometry ChevronRight { get; } = Parse("M9.5 6.5l5.5 5.5-5.5 5.5");

    public static Geometry Check { get; } = Parse("M5 12.5l4.5 4.5L19 7");

    public static Geometry Copy { get; } = Parse(
        "M9.5 8.5h9A1.5 1.5 0 0 1 20 10v9.5a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 8 19.5V10" +
        "a1.5 1.5 0 0 1 1.5-1.5zM5.5 15.5H5a1.5 1.5 0 0 1-1.5-1.5V4.5A1.5 1.5 0 0 1 5 3h9.5" +
        "A1.5 1.5 0 0 1 16 4.5V5");

    public static Geometry Eye { get; } = Parse(
        "M2.6 12S6.2 5.8 12 5.8 21.4 12 21.4 12 17.8 18.2 12 18.2 2.6 12 2.6 12z" +
        "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z");

    public static Geometry EyeOff { get; } = Parse(
        "M3.5 3.5l17 17M10.6 6.1A9.9 9.9 0 0 1 12 6c5.8 0 9.4 6 9.4 6" +
        "a16.4 16.4 0 0 1-3.4 4M6.3 8.1A16.8 16.8 0 0 0 2.6 12S6.2 18 12 18" +
        "a10 10 0 0 0 3.6-.7M9.9 9.9a3 3 0 0 0 4.2 4.2");

    public static Geometry Download { get; } = Parse(
        "M12 3.5v11M7.5 10.5L12 15l4.5-4.5M4 17v2.5A1.5 1.5 0 0 0 5.5 21h13a1.5 1.5 0 0 0 1.5-1.5V17");

    public static Geometry Search { get; } = Parse(
        "M11 17.5a6.5 6.5 0 1 0 0-13 6.5 6.5 0 0 0 0 13zM16 16l4.5 4.5");

    public static Geometry Minimize { get; } = Parse("M5 12h14");

    public static Geometry Maximize { get; } = Parse("M5.5 5.5h13v13h-13z");

    public static Geometry Close { get; } = Parse("M6 6l12 12M18 6L6 18");

    public static Geometry Alert { get; } = Parse("M12 3.5L21.5 20H2.5zM12 9.5v4.5M12 17h.01");

    public static Geometry Commit { get; } = Parse(
        "M12 8.5a3.5 3.5 0 1 1 0 7 3.5 3.5 0 0 1 0-7zM12 3v5.5M12 15.5V21");

    public static Geometry Diff { get; } = Parse(
        "M6 8h9M6 8l3-3M6 8l3 3M18 16h-9M18 16l-3-3M18 16l-3 3");

    public static Geometry Shield { get; } = Parse(
        "M12 3l7.5 3v5.5c0 4.6-3 7.8-7.5 9.5-4.5-1.7-7.5-4.9-7.5-9.5V6z");

    public static Geometry Logo { get; } = Parse("M12 3.2l8.8 8.8-8.8 8.8L3.2 12z");

    public static Geometry Globe { get; } = Parse(
        "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM3.5 12h17" +
        "M12 3c2.4 2.6 3.7 5.7 3.7 9s-1.3 6.4-3.7 9c-2.4-2.6-3.7-5.7-3.7-9S9.6 5.6 12 3z");

    /// <summary>亮色主题。</summary>
    public static Geometry Sun { get; } = Parse(
        "M12 8.4a3.6 3.6 0 1 0 0 7.2 3.6 3.6 0 0 0 0-7.2z" +
        "M12 3.4v2.1M12 18.5v2.1M3.4 12h2.1M18.5 12h2.1" +
        "M6.1 6.1l1.5 1.5M16.4 16.4l1.5 1.5M17.9 6.1l-1.5 1.5M7.6 16.4l-1.5 1.5");

    /// <summary>暗色主题。</summary>
    public static Geometry Moon { get; } = Parse(
        "M20 13.6A8.2 8.2 0 0 1 10.4 4a8.3 8.3 0 1 0 9.6 9.6z");

    /// <summary>跟随系统。</summary>
    public static Geometry Monitor { get; } = Parse(
        "M3.5 5.2h17v10.4h-17zM9 19.6h6M12 15.6v4");

    /// <summary>GitHub 标记（实心路径，用 Fill 绘制）。</summary>
    public static Geometry GitHub { get; } = Parse(
        "M12 .5C5.73.5.5 5.73.5 12c0 5.08 3.29 9.39 7.86 10.91.58.11.79-.25.79-.56" +
        " 0-.28-.01-1.02-.02-2-3.2.7-3.88-1.54-3.88-1.54-.53-1.34-1.3-1.7-1.3-1.7" +
        "-1.06-.72.08-.71.08-.71 1.17.08 1.79 1.2 1.79 1.2 1.04 1.78 2.73 1.27 3.4.97" +
        ".11-.75.41-1.27.74-1.56-2.55-.29-5.24-1.28-5.24-5.69 0-1.26.45-2.29 1.19-3.1" +
        "-.12-.29-.52-1.46.11-3.05 0 0 .97-.31 3.18 1.18a11.1 11.1 0 0 1 5.8 0" +
        "c2.2-1.49 3.17-1.18 3.17-1.18.63 1.59.23 2.76.11 3.05.74.81 1.19 1.84 1.19 3.1" +
        " 0 4.42-2.69 5.39-5.25 5.68.42.36.79 1.08.79 2.18 0 1.57-.01 2.84-.01 3.23" +
        " 0 .31.21.68.8.56C20.71 21.39 24 17.08 24 12c0-6.27-5.23-11.5-11.5-11.5z");

    private static Geometry Parse(string data) => Geometry.Parse(data);
}
