namespace TaTaTask.Services;

/// <summary>
/// 时间基准的统一入口。
///
/// 约定（全项目必须遵守）：
/// 1. 数据库统一存 <b>UTC</b>。
/// 2. API 边界（DTO 出参 / 入参）一律使用 <b>用户时区下的墙钟时间</b>（Kind = Unspecified），
///    由服务端在边界处换算，客户端原样显示、原样提交。
/// 3. 「今天」「本周」「时段是否已到」等日历口径按用户时区计算，不用服务器本地时区、也不用 UTC 日期。
/// </summary>
public static class UserTime
{
    public const string DefaultTimeZoneId = "Asia/Shanghai";

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, TimeZoneInfo> Cache = new();

    /// <summary>解析 IANA 时区 ID；无法识别时回落到默认时区，再回落到 UTC。</summary>
    public static TimeZoneInfo Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Default();
        }

        return Cache.GetOrAdd(id, static key =>
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(key);
            }
            catch (TimeZoneNotFoundException)
            {
                return Default();
            }
            catch (InvalidTimeZoneException)
            {
                return Default();
            }
        });
    }

    private static TimeZoneInfo Default()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>用户时区墙钟时间 → UTC。</summary>
    public static DateTime ToUtc(DateTime userLocal, TimeZoneInfo tz)
    {
        var unspecified = DateTime.SpecifyKind(userLocal, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
    }

    public static DateTime? ToUtc(DateTime? userLocal, TimeZoneInfo tz)
        => userLocal.HasValue ? ToUtc(userLocal.Value, tz) : null;

    /// <summary>UTC → 用户时区墙钟时间（Kind = Unspecified，可直接 JSON 给客户端）。</summary>
    public static DateTime ToUser(DateTime utc, TimeZoneInfo tz)
        => TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), tz);

    public static DateTime? ToUser(DateTime? utc, TimeZoneInfo tz)
        => utc.HasValue ? ToUser(utc.Value, tz) : null;

    /// <summary>把数据库读出的时间标记为 UTC（SQLite 不保存 Kind）。</summary>
    public static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static DateTime? AsUtc(DateTime? value)
        => value.HasValue ? AsUtc(value.Value) : null;

    /// <summary>用户时区下的「今天」。</summary>
    public static DateOnly Today(TimeZoneInfo tz)
        => DateOnly.FromDateTime(ToUser(DateTime.UtcNow, tz));

    /// <summary>用户时区下某个日历日对应的 UTC 区间 [StartUtc, EndUtc)。</summary>
    public static (DateTime StartUtc, DateTime EndUtc) DayRangeUtc(DateOnly date, TimeZoneInfo tz)
    {
        var startLocal = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var endLocal = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (ToUtc(startLocal, tz), ToUtc(endLocal, tz));
    }

    /// <summary>可选的常用时区（设置页下拉用）。</summary>
    public static readonly (string Id, string Label)[] CommonTimeZones =
    [
        ("Asia/Shanghai", "中国标准时间 (UTC+8)"),
        ("Asia/Hong_Kong", "香港 (UTC+8)"),
        ("Asia/Taipei", "台北 (UTC+8)"),
        ("Asia/Singapore", "新加坡 (UTC+8)"),
        ("Asia/Tokyo", "东京 (UTC+9)"),
        ("Asia/Seoul", "首尔 (UTC+9)"),
        ("Asia/Bangkok", "曼谷 (UTC+7)"),
        ("Asia/Kolkata", "印度 (UTC+5:30)"),
        ("Asia/Dubai", "迪拜 (UTC+4)"),
        ("Europe/London", "伦敦 (UTC+0/+1)"),
        ("Europe/Paris", "巴黎 (UTC+1/+2)"),
        ("Europe/Moscow", "莫斯科 (UTC+3)"),
        ("America/New_York", "纽约 (UTC-5/-4)"),
        ("America/Chicago", "芝加哥 (UTC-6/-5)"),
        ("America/Los_Angeles", "洛杉矶 (UTC-8/-7)"),
        ("Australia/Sydney", "悉尼 (UTC+10/+11)"),
        ("UTC", "协调世界时 (UTC)"),
    ];

    /// <summary>时区 ID 是否可被系统识别。</summary>
    public static bool IsValid(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
