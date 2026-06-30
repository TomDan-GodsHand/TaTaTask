namespace TaTaTask.Models.Dtos;

public class UserSettingsDto
{
    public string Username { get; set; } = string.Empty;
    public int DefaultDueWarningHours { get; set; }
    public int DefaultDueDays { get; set; }
}

public class UpdateUserSettingsRequest
{
    public string? Username { get; set; }
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
    public int? DefaultDueWarningHours { get; set; }
    public int? DefaultDueDays { get; set; }
}
