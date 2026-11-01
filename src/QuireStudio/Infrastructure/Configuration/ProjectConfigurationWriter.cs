using System.Text;
using System.Text.RegularExpressions;
using QuireStudio.Domain;

namespace QuireStudio.Infrastructure.Configuration;

/// <summary>要写回 YAML 的项目级字段。</summary>
public sealed record ProjectConfigurationUpdate
{
    public string? ProjectName { get; init; }

    public string? Output { get; init; }

    public string? Suffix { get; init; }

    /// <summary>默认压缩算法：<c>none</c> / <c>lz4</c> / <c>zlib</c>。</summary>
    public string? Compression { get; init; }

    /// <summary>压缩策略：<c>always</c> / <c>adaptive</c>。</summary>
    public string? CompressionPolicy { get; init; }

    public string? SecretKey { get; init; }

    public int? Jobs { get; init; }
}

/// <summary>
/// 就地修改 <c>quire.*.yml</c> 中的少量标量字段。
///
/// <para>
/// 采用逐行改写而不是整份反序列化再写回，因此注释、字段顺序与 GUI 不认识的
/// 新字段都会被原样保留，不会把用户的配置文件"洗"成另一种样子。
/// 只处理顶级字段与 <c>build</c> 段内的 <c>jobs</c>，其余内容一概不碰。
/// </para>
/// </summary>
public sealed partial class ProjectConfigurationWriter
{
    /// <summary>写入更新；成功返回 <see langword="null"/>，失败返回可展示的错误信息。</summary>
    public string? Update(string filePath, ProjectConfigurationUpdate update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(update);

        try
        {
            if (!File.Exists(filePath))
            {
                return $"配置文件不存在：{filePath}";
            }

            string original = File.ReadAllText(filePath);
            bool crlf = original.Contains("\r\n", StringComparison.Ordinal);
            List<string> lines = [.. original.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')];
            int originalLength = lines.Count;

            bool changed = false;

            if (Normalize(update.ProjectName) is { } projectName)
            {
                changed |= SetTopLevel(lines, "project", Quote(projectName));
            }

            if (Normalize(update.Output) is { } output)
            {
                changed |= SetTopLevel(lines, "output", Quote(output));
            }

            if (Normalize(update.Suffix) is { } suffix)
            {
                changed |= SetTopLevel(lines, "suffix", Quote(suffix));
            }

            if (Normalize(update.Compression) is { } compression)
            {
                changed |= SetTopLevel(lines, "compression", Token(compression));
            }

            if (Normalize(update.CompressionPolicy) is { } policy)
            {
                changed |= SetTopLevel(lines, "compression-policy", Token(policy));
            }

            if (update.SecretKey is not null)
            {
                string key = update.SecretKey.Trim();
                changed |= SetTopLevel(
                    lines,
                    "secret-key",
                    ProjectConfiguration.IsDisabledKey(key) ? "none" : Quote(key));
            }

            if (update.Jobs is { } jobs)
            {
                changed |= SetNested(lines, "build", "jobs", jobs.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            // 末尾追加时补一个空行，避免和最后一行粘在一起。
            if (lines.Count > originalLength && originalLength > 0 && lines[originalLength - 1].Length > 0)
            {
                lines.Insert(originalLength, "");
            }

            if (!changed)
            {
                return null;
            }

            string text = string.Join(crlf ? "\r\n" : "\n", lines);
            WriteAtomic(filePath, text);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return $"写入配置文件失败：{exception.Message}";
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>枚举式取值（lz4 / adaptive 等）保持裸写，和 CLI 生成的配置一致。</summary>
    private static string Token(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z0-9_-]+$")
            ? value
            : Quote(value);

    private static string Quote(string value)
    {
        string cleaned = value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return "\"" + cleaned.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>替换顶级 <paramref name="key"/> 的值；缺失时追加到文件末尾。</summary>
    private static bool SetTopLevel(List<string> lines, string key, string value)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            Match match = TopLevelPattern(key).Match(lines[index]);
            if (!match.Success)
            {
                continue;
            }

            string updated = $"{key}: {value}{TrailingComment(match.Groups["rest"].Value)}";
            if (string.Equals(updated, lines[index], StringComparison.Ordinal))
            {
                return false;
            }

            lines[index] = updated;
            return true;
        }

        lines.Add($"{key}: {value}");
        return true;
    }

    /// <summary>在 <paramref name="parent"/> 段内替换子字段；缺失时插到父字段下一行。</summary>
    private static bool SetNested(List<string> lines, string parent, string key, string value)
    {
        int parentIndex = -1;
        for (int index = 0; index < lines.Count; index++)
        {
            if (TopLevelPattern(parent).IsMatch(lines[index]))
            {
                parentIndex = index;
                break;
            }
        }

        if (parentIndex < 0)
        {
            lines.Add($"{parent}:");
            lines.Add($"  {key}: {value}");
            return true;
        }

        int blockEnd = lines.Count;
        for (int index = parentIndex + 1; index < lines.Count; index++)
        {
            string line = lines[index];
            if (line.Trim().Length == 0)
            {
                continue;
            }

            if (line[0] != ' ' && line[0] != '\t' && line[0] != '#')
            {
                blockEnd = index;
                break;
            }
        }

        for (int index = parentIndex + 1; index < blockEnd; index++)
        {
            Match match = NestedPattern(key).Match(lines[index]);
            if (!match.Success)
            {
                continue;
            }

            string indent = match.Groups["indent"].Value;
            string updated = $"{indent}{key}: {value}{TrailingComment(match.Groups["rest"].Value)}";
            if (string.Equals(updated, lines[index], StringComparison.Ordinal))
            {
                return false;
            }

            lines[index] = updated;
            return true;
        }

        lines.Insert(parentIndex + 1, $"  {key}: {value}");
        return true;
    }

    /// <summary>保留行尾注释（引号内的 <c>#</c> 不算注释）。</summary>
    private static string TrailingComment(string rest)
    {
        bool single = false;
        bool quote = false;

        for (int index = 0; index < rest.Length; index++)
        {
            char current = rest[index];
            if (current == '\'' && !quote)
            {
                single = !single;
            }
            else if (current == '"' && !single)
            {
                quote = !quote;
            }
            else if (current == '#' && !single && !quote &&
                     (index == 0 || char.IsWhiteSpace(rest[index - 1])))
            {
                return " " + rest[index..].TrimEnd();
            }
        }

        return "";
    }

    private static void WriteAtomic(string filePath, string text)
    {
        string temp = filePath + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));

        if (File.Exists(filePath))
        {
            File.Replace(temp, filePath, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, filePath);
        }
    }

    private static Regex TopLevelPattern(string key) =>
        new($@"^(?<indent>[ ]*){Regex.Escape(key)}[ ]*:(?<rest>.*)$", RegexOptions.IgnoreCase);

    private static Regex NestedPattern(string key) =>
        new($@"^(?<indent>[ \t]+){Regex.Escape(key)}[ ]*:(?<rest>.*)$", RegexOptions.IgnoreCase);
}
