using System.Globalization;

namespace QuireStudio.Presentation;

/// <summary>界面上的通用格式化。</summary>
public static class Formatting
{
    /// <summary>把 CLI 的阶段标识转换为可读文案。</summary>
    public static string Stage(string? stageId) => stageId switch
    {
        "preparing" => "准备中",
        "processing" => "处理中",
        "discovering" => "扫描源文件",
        "hashing" => "计算摘要",
        "writing" => "写入资源包",
        "opening" => "打开资源包",
        "indexing" => "建立索引",
        "preparing_verification" => "准备校验",
        "verifying" => "校验内容",
        "publishing" => "生成构建记录",
        "preparing_extraction" => "准备解包",
        "extracting" => "提取文件",
        "publishing_extraction" => "写入目标目录",
        null or "" => "处理中",
        _ => stageId.Replace('_', ' '),
    };

    /// <summary>把字节数格式化为常用单位。</summary>
    public static string Bytes(long value)
    {
        const double Kilo = 1024d;

        if (value < Kilo)
        {
            return $"{value} B";
        }

        double scaled = value / Kilo;
        string[] units = ["KiB", "MiB", "GiB", "TiB"];
        int unitIndex = 0;
        while (scaled >= Kilo && unitIndex < units.Length - 1)
        {
            scaled /= Kilo;
            unitIndex++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{scaled:0.#} {units[unitIndex]}");
    }

    /// <summary>千位分隔的整数。</summary>
    public static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>相对时间描述，例如「3 小时前」。</summary>
    public static string RelativeTime(DateTime value)
    {
        TimeSpan delta = DateTime.Now - value;
        if (delta.TotalSeconds < 60)
        {
            return "刚刚";
        }

        if (delta.TotalMinutes < 60)
        {
            return $"{(int)delta.TotalMinutes} 分钟前";
        }

        if (delta.TotalHours < 24)
        {
            return $"{(int)delta.TotalHours} 小时前";
        }

        if (delta.TotalDays < 30)
        {
            return $"{(int)delta.TotalDays} 天前";
        }

        return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
