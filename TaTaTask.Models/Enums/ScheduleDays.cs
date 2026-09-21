namespace TaTaTask.Models.Enums;

/// <summary>
/// 周几的位掩码工具：周一 = bit0 … 周日 = bit6（与 <see cref="ScheduleRule.DaysOfWeek"/> 一致）。
/// 放在 Models 里，供服务端与 WASM 客户端共用。
/// </summary>
public static class ScheduleDays
{
    public const int All = 0b111_1111;

    /// <summary>某个星期几对应的位。Sunday=0 … Saturday=6，映射到 周一=bit0 … 周日=bit6。</summary>
    public static int Bit(DayOfWeek day) => 1 << (((int)day + 6) % 7);

    public static bool Has(int mask, DayOfWeek day) => (mask & Bit(day)) != 0;

    /// <summary>把掩码展开为按周一到周日排序的星期几。</summary>
    public static IEnumerable<DayOfWeek> Days(int mask)
    {
        for (int bit = 0; bit < 7; bit++)
        {
            if ((mask & (1 << bit)) == 0) continue;
            // bit0 = Monday(1) … bit6 = Sunday(0)
            yield return (DayOfWeek)((bit + 1) % 7);
        }
    }

    private static readonly string[] Names = ["周一", "周二", "周三", "周四", "周五", "周六", "周日"];

    /// <summary>紧凑标签，如「周一/三/五」「每天」「工作日」。</summary>
    public static string Label(int mask)
    {
        if (mask == 0) return "未设置";
        if (mask == All) return "每天";
        if (mask == 0b001_1111) return "工作日";
        if (mask == 0b110_0000) return "周末";
        return string.Join("/", Days(mask).Select(d => Names[((int)d + 6) % 7]));
    }
}
