using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TaTaTask.Api;

/// <summary>对外接口的业务异常，携带要返回的 HTTP 状态码。</summary>
public sealed class ApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public static ApiException BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);

    public static ApiException NotFound(string message = "记录不存在") => new(StatusCodes.Status404NotFound, message);

    public static ApiException Conflict(string message) => new(StatusCodes.Status409Conflict, message);

    public static ApiException Forbidden(string message = "当前令牌没有写入权限") => new(StatusCodes.Status403Forbidden, message);
}

/// <summary>把业务异常翻译成统一的 ProblemDetails 响应。</summary>
public sealed class ApiExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var (status, title) = context.Exception switch
        {
            ApiException ex => (ex.StatusCode, ex.Message),
            KeyNotFoundException ex => (StatusCodes.Status404NotFound, ex.Message),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "需要有效的访问令牌"),
            InvalidOperationException ex => (StatusCodes.Status409Conflict, ex.Message),
            ArgumentException ex => (StatusCodes.Status400BadRequest, ex.Message),
            _ => (0, string.Empty),
        };

        if (status == 0) return;

        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = status,
            Title = title,
            Instance = context.HttpContext.Request.Path,
        })
        {
            StatusCode = status,
        };
        context.ExceptionHandled = true;
    }
}
