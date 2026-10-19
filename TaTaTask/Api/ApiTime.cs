using TaTaTask.Services;

namespace TaTaTask.Api;

/// <summary>
/// 对外接口的时间边界：数据库存 UTC，对外输出带用户时区偏移的 ISO 8601（DateTimeOffset），
/// 输入接受任意偏移并统一换算为 UTC。与网页内部 DTO 的「用户墙钟时间」约定相互独立。
/// </summary>
public static class ApiTime
{
    /// <summary>库中 UTC → 带用户时区偏移的时间。</summary>
    public static DateTimeOffset ToOffset(DateTime utc, TimeZoneInfo tz)
        => TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), tz);

    public static DateTimeOffset? ToOffset(DateTime? utc, TimeZoneInfo tz)
        => utc.HasValue ? ToOffset(utc.Value, tz) : null;

    /// <summary>对外时间 → 库中 UTC。</summary>
    public static DateTime ToUtc(DateTimeOffset value) => value.UtcDateTime;

    public static DateTime? ToUtc(DateTimeOffset? value) => value?.UtcDateTime;

    /// <summary>把内部 DTO 里的用户墙钟时间（Kind = Unspecified）转成带偏移的时间。</summary>
    public static DateTimeOffset WallClockToOffset(DateTime wallClock, TimeZoneInfo tz)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, tz.GetUtcOffset(unspecified));
    }

    public static DateTimeOffset? WallClockToOffset(DateTime? wallClock, TimeZoneInfo tz)
        => wallClock.HasValue ? WallClockToOffset(wallClock.Value, tz) : null;

    /// <summary>库中 UTC → 用户墙钟时间（给需要墙钟的旧接口用）。</summary>
    public static DateTime ToWallClock(DateTime utc, TimeZoneInfo tz) => UserTime.ToUser(utc, tz);

    /// <summary>解析 <c>HH:mm</c> 或 <c>HH:mm:ss</c>。</summary>
    public static bool TryParseTime(string? text, out TimeOnly value)
        => TimeOnly.TryParseExact(text, ["HH:mm", "HH:mm:ss"],
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out value);

    public static TimeOnly ParseTime(string? text, TimeOnly defaultValue)
        => text is null ? defaultValue : TryParseTime(text, out var value)
            ? value : throw ApiException.BadRequest("时刻需为 HH:mm 或 HH:mm:ss");

    public static string FormatTime(TimeOnly value) => value.ToString(value.Second == 0 ? "HH:mm" : "HH:mm:ss");

    /// <summary>解析 <c>yyyy-MM-dd</c> 日期。</summary>
    public static bool TryParseDate(string? text, out DateOnly value)
        => DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out value);
}
