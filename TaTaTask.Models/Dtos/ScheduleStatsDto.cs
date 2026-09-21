using TaTaTask.Models.Enums;

namespace TaTaTask.Models.Dtos;

/// <summary>日程执行统计（并入 /stats 页，不新开页面）。口径全部在查询时计算。</summary>
public class ScheduleStatsDto
{
    // ---------- 指标卡 ----------
    public int TodayDone { get; set; }
    /// <summary>今日有安排的条目数（不含「跳过」），作为完成率分母。</summary>
    public int TodayPlanned { get; set; }

    public int Last30Done { get; set; }
    public int Last30Planned { get; set; }
    public int Prev30Done { get; set; }
    public int Prev30Planned { get; set; }

    /// <summary>连续全勤天数：往前逐日，有安排且当天全部完成才算一天；没安排的日子跳过不打断。</summary>
    public int StreakDays { get; set; }

    /// <summary>近 30 天平均每天排了多少分钟。</summary>
    public int AvgDailyMinutes { get; set; }

    // ---------- 图表：近 8 周排定 / 完成 ----------
    public List<WeeklyExecutionDto> WeeklyExecution { get; set; } = new();

    // ---------- 列表：规则执行榜 ----------
    public List<RulePerformanceDto> Rules { get; set; } = new();

    /// <summary>一条可行动的建议（指出哪条规则排得不合理）。</summary>
    public string? Insight { get; set; }

    public bool HasData => Last30Planned > 0 || TodayPlanned > 0 || Prev30Planned > 0;

    public int TodayRate => Rate(TodayDone, TodayPlanned);
    public int Last30Rate => Rate(Last30Done, Last30Planned);
    public int Prev30Rate => Rate(Prev30Done, Prev30Planned);

    /// <summary>与上一个 30 天相比的百分点变化。</summary>
    public int Last30Delta => Last30Planned == 0 && Prev30Planned == 0 ? 0 : Last30Rate - Prev30Rate;

    private static int Rate(int done, int planned)
        => planned <= 0 ? 0 : (int)Math.Round(done * 100.0 / planned);
}

public class WeeklyExecutionDto
{
    public string WeekLabel { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public int Planned { get; set; }
    public int Done { get; set; }
}

public class RulePerformanceDto
{
    public int RuleId { get; set; }
    public string Title { get; set; } = string.Empty;
    public ScheduleRuleMode Mode { get; set; }
    public string TimeLabel { get; set; } = string.Empty;

    public int Planned { get; set; }
    public int Done { get; set; }
    public int Skipped { get; set; }

    public int Rate => Planned <= 0 ? 0 : (int)Math.Round(Done * 100.0 / Planned);
    public string ModeLabel => Mode == ScheduleRuleMode.Generate ? "生成" : "挑选";
}
