using TaTaTask.Models.Enums;

namespace TaTaTask.Models.Dtos;

public class CreateTodoRequest
{
    public string Title { get; set; } = string.Empty;
    public TodoStatus Status { get; set; } = TodoStatus.NotStarted;
    public string? Description { get; set; }
    public int Priority { get; set; }
    public string? Tags { get; set; }
    public DateTime? DueDate { get; set; }
    public int? DueWarningHours { get; set; }
    public List<string>? Steps { get; set; }
}

public class ChangeStatusRequest
{
    public TodoStatus Status { get; set; }
}
