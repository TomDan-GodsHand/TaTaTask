using System.ComponentModel.DataAnnotations;

namespace TaTaTask.Models.Dtos;

public class FeedbackReplyRequest
{
    [Required(ErrorMessage = "回复内容不能为空")]
    [MaxLength(5000)]
    public string Content { get; set; } = string.Empty;
}
