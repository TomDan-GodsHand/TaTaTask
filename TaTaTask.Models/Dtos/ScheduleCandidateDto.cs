using TaTaTask.Models.Enums;

namespace TaTaTask.Models.Dtos;

/// <summary>挑选模式的候选任务（来自看板未完成任务池）。</summary>
public class ScheduleCandidateDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public int Priority { get; set; }
    public DateTime? DueDate { get; set; }
    public TodoStatus Status { get; set; }

    public List<TodoStepDto> Steps { get; set; } = new();
    public int StepCount => Steps.Count;
    public int DoneStepCount => Steps.Count(s => s.IsCompleted);
}

/// <summary>把某个候选任务排进这条日程项，并选定今天要做哪些子步骤。</summary>
public class PickScheduleTaskRequest
{
    public int TodoItemId { get; set; }

    /// <summary>今天要做的子步骤；空表示全部。</summary>
    public List<int> StepIds { get; set; } = new();
}

/// <summary>改今天排定的子步骤。</summary>
public class SetEntryStepsRequest
{
    public List<int> StepIds { get; set; } = new();
}

/// <summary>手动加一条临时日程项（不属于任何规则）。</summary>
public class CreateManualEntryRequest
{
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    /// <summary>可选的标签：落成「待选」时用它筛候选任务。</summary>
    public string? Tags { get; set; }

    /// <summary>可选的事项标题：填了就自动建一张看板卡并直接排上；不填则留作「待选」稍后挑。</summary>
    public string? Title { get; set; }
}
