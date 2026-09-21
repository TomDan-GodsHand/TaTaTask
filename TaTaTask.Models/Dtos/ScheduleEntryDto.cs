using TaTaTask.Models.Enums;

namespace TaTaTask.Models.Dtos;

/// <summary>某个日历日、某个时段上的一条日程项。</summary>
public class ScheduleEntryDto
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }

    public int? RuleId { get; set; }
    public string RuleTitle { get; set; } = string.Empty;
    public ScheduleRuleMode? RuleMode { get; set; }

    public int? TodoItemId { get; set; }
    /// <summary>任务标题；任务被删除后为快照。</summary>
    public string Title { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public TodoStatus? TodoStatus { get; set; }

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public ScheduleEntryState State { get; set; }

    /// <summary>关联任务已被硬删除的留痕标记。</summary>
    public bool IsTaskDeleted { get; set; }

    /// <summary>关联任务由本条安排自动创建（删除安排时可一并删除）。</summary>
    public bool TaskCreatedHere { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>今日排定的子步骤（按推进顺序）。</summary>
    public List<ScheduleEntryStepDto> Steps { get; set; } = new();

    /// <summary>关联任务的全部子步骤（含今天没排的），用于「改今日步骤」。</summary>
    public List<TodoStepDto> TaskSteps { get; set; } = new();

    public string TimeLabel => $"{StartTime:HH\\:mm}–{EndTime:HH\\:mm}";
    public int TotalSteps => Steps.Count;
    public int DoneSteps => Steps.Count(s => s.IsCompleted);

    /// <summary>侧边栏一次只显示一个：下一个待做步骤。</summary>
    public ScheduleEntryStepDto? CurrentStep =>
        Steps.Where(s => !s.IsCompleted).OrderBy(s => s.SortOrder).FirstOrDefault();
}

public class ScheduleEntryStepDto
{
    public int Id { get; set; }
    public int TodoStepId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>今日日程整体（侧边抽屉与日程页共用）。</summary>
public class TodayScheduleDto
{
    public DateOnly Date { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;

    /// <summary>今天的日程项，按时段升序。</summary>
    public List<ScheduleEntryDto> Entries { get; set; } = new();

    /// <summary>今天之前的未完成项（待处置：排进今天 / 冻结 / 删除）。</summary>
    public List<ScheduleEntryDto> Unfinished { get; set; } = new();

    /// <summary>当前时段命中的日程项 Id；没有则为空。</summary>
    public int? CurrentEntryId { get; set; }

    /// <summary>
    /// 服务端此刻在用户时区下的墙钟时间。客户端算「还剩 N 分钟」用它，
    /// 而不是浏览器本地时间——否则用户配的时区和浏览器时区不一致时会算错。
    /// </summary>
    public TimeOnly NowTime { get; set; }

    /// <summary>今天还有多少条待选（挑选模式还没挑任务）。</summary>
    public int PendingCount => Entries.Count(e => e.State == ScheduleEntryState.Pending);
}

/// <summary>归档页的日程历史（按天聚合）。</summary>
public class ScheduleHistoryDto
{
    public DateOnly Date { get; set; }
    public List<ScheduleEntryDto> Entries { get; set; } = new();

    /// <summary>当天有安排的条目数（不含「跳过」），作为完成率分母。</summary>
    public int Total => Entries.Count(e => e.State != ScheduleEntryState.Skipped);
    public int Completed => Entries.Count(e => e.State == ScheduleEntryState.Completed);
    public int Skipped => Entries.Count(e => e.State == ScheduleEntryState.Skipped);

    /// <summary>当天全部条目数（含跳过），用于展示。</summary>
    public int AllCount => Entries.Count;
}
