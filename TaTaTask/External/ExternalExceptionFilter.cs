using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TaTaTask.External;

public sealed class ExternalExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ExternalApiException error) return;
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = error.Status, Title = error.Message, Instance = context.HttpContext.Request.Path,
        }) { StatusCode = error.Status };
        context.ExceptionHandled = true;
    }
}
