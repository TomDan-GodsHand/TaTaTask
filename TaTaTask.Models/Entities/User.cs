namespace TaTaTask.Models.Entities;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public int DefaultDueWarningHours { get; set; } = 48;
    public int DefaultDueDays { get; set; } = 3;

    /// <summary>IANA 时区 ID。「今天」等日历口径按它计算，数据库统一存 UTC。</summary>
    public string TimeZoneId { get; set; } = "Asia/Shanghai";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TodoItem> TodoItems { get; set; } = new List<TodoItem>();
}
