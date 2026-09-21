namespace TaTaTask.Models;

/// <summary>标签的规范化与匹配。现有 Tags 是逗号分隔的自由文本（中英文逗号都支持）。</summary>
public static class TagUtil
{
    private static readonly char[] Separators = [',', '，'];

    /// <summary>拆分为去空、去重的标签数组。</summary>
    public static string[] Split(string? tags)
        => string.IsNullOrWhiteSpace(tags)
            ? []
            : tags.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>规范化为「逗号分隔、无空格」的存储形式；空则返回 null。</summary>
    public static string? Normalize(string? tags)
    {
        var parts = Split(tags);
        return parts.Length == 0 ? null : string.Join(",", parts);
    }

    /// <summary>整标签精确匹配（大小写不敏感）——避免「工作」命中「工作A」。</summary>
    public static bool Contains(string? tags, string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;
        return Split(tags).Any(t => string.Equals(t, tag.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>是否命中任意一个期望标签。</summary>
    public static bool ContainsAny(string? tags, IEnumerable<string> wanted)
        => wanted.Any(w => Contains(tags, w));
}
