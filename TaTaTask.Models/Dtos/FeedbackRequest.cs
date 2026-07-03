using System.ComponentModel.DataAnnotations;

namespace TaTaTask.Models.Dtos;

public class FeedbackRequest
{
    [Required(ErrorMessage = "标题不能为空")]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "内容不能为空")]
    [MaxLength(5000)]
    public string Content { get; set; } = string.Empty;
}
