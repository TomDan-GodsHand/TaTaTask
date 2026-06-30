using System.ComponentModel.DataAnnotations;

namespace TaTaTask.Models.Dtos;

public class RegisterRequest
{
    [Required]
    [StringLength(50, ErrorMessage = "用户名最长 50 字符")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "密码至少 8 字符")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(Password), ErrorMessage = "两次输入的密码不一致")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
