using System.ComponentModel.DataAnnotations;
using TaTaTask.Models.Enums;

namespace TaTaTask.Models.Dtos;

public class TaskQuery
{
    [Range(1, 1000000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
    [StringLength(200)] public string? Q { get; set; }
    [StringLength(500)] public string? Tag { get; set; }
    public TodoStatus? Status { get; set; }
    [Range(0, 4)] public int? Priority { get; set; }
    public bool? Archived { get; set; } = false;
    // updated (descending), created (descending), priority (descending), due (ascending, null last).
    public string Sort { get; set; } = "updated";
}

public record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize, string TimeZoneId);

public class SetStepCompletionRequest
{
    [Required] public bool? Completed { get; set; }
}
