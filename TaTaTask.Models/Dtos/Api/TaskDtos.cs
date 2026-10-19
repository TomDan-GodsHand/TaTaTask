namespace TaTaTask.Models.Dtos.Api;

/// <summary>
/// 对外任务视图。所有时间字段都是带时区偏移的 ISO 8601（例如 <c>2026-10-19T09:00:00+08:00</c>）。
/// </summary>
public class TaskDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Priority { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public DateTimeOffset? DueDate { get; set; }
    public int? DueWarningHours { get; set; }

    /// <summary>notStarted / inProgress / converging / done / frozen</summary>
    public string Status { get; set; } = "notStarted";

    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public string? PreviousStatus { get; set; }
    public string? FrozenReason { get; set; }
    public DateTimeOffset? FrozeAt { get; set; }
    public int? SourceRuleId { get; set; }
    public string? SourceRuleTitle { get; set; }
    public List<TaskStepDto> Steps { get; set; } = new();

    public int TotalSteps => Steps.Count;
    public int DoneSteps => Steps.Count(s => s.IsCompleted);
    public int? CurrentStepId => Steps.Where(s => !s.IsCompleted).OrderBy(s => s.SortOrder).Select(s => (int?)s.Id).FirstOrDefault();
}

/// <summary>任务的子步骤。</summary>
public class TaskStepDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>创建任务。</summary>
public class CreateTaskRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Priority { get; set; }

    /// <summary>标签数组，例如 <c>["工作", "本周"]</c>。</summary>
    public string[]? Tags { get; set; }

    public DateTimeOffset? DueDate { get; set; }
    public int? DueWarningHours { get; set; }

    /// <summary>初始状态；默认 notStarted。不能指定 frozen；带步骤时不能直接指定 done。</summary>
    public string? Status { get; set; }

    /// <summary>子步骤标题，按顺序创建。</summary>
    public List<string>? Steps { get; set; }
}

/// <summary>
/// 局部更新任务：只有出现的字段才会被修改。
/// <see cref="Tags"/> 为空数组表示清空标签；<see cref="ClearDueDate"/> 为 true 表示清空截止时间。
/// </summary>
public class PatchTaskRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public int? Priority { get; set; }
    public string[]? Tags { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public bool ClearDueDate { get; set; }
    public int? DueWarningHours { get; set; }
    public List<string>? StepsToAdd { get; set; }
    public List<int>? StepIdsToDelete { get; set; }
    public List<int>? StepsOrder { get; set; }
}

/// <summary>状态流转。</summary>
public class ChangeTaskStatusRequest
{
    /// <summary>notStarted / inProgress / converging / done / frozen</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>进入 frozen 时的原因。</summary>
    public string? FrozenReason { get; set; }

    /// <summary>流转时是否重置所有子步骤。</summary>
    public bool ResetSteps { get; set; }
}

/// <summary>新增/重命名单个子步骤。</summary>
public class TaskStepRequest
{
    public string Title { get; set; } = string.Empty;
}

/// <summary>重排子步骤。</summary>
public class ReorderStepsRequest
{
    public List<int> StepIds { get; set; } = new();
}

/// <summary>显式设置子步骤完成状态（比 toggle 更适合幂等重试）。</summary>
public class StepCompletionRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    public bool? Completed { get; set; }
}
