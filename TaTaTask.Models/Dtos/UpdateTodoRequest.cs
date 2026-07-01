namespace TaTaTask.Models.Dtos;

public class UpdateTodoRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Priority { get; set; }
    public string? Tags { get; set; }
    public DateTime? DueDate { get; set; }
    public int? DueWarningHours { get; set; }
    public List<string>? StepsToAdd { get; set; }
    public List<int>? StepIdsToDelete { get; set; }
}
