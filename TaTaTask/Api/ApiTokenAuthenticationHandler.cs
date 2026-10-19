using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace TaTaTask.Api;

/// <summary>
/// 只用于显式声明外部策略的入口；网页与旧 API 的默认认证始终为 Cookie。
/// </summary>
public sealed class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiTokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var raw = header["Bearer ".Length..].Trim();
        if (raw.Length < ApiTokenDefaults.TokenPrefix.Length + 8)
            return AuthenticateResult.Fail("访问令牌格式不正确");

        var principal = await tokens.AuthenticateAsync(raw, Context.RequestAborted);
        if (principal is null)
            return AuthenticateResult.Fail("访问令牌无效、已过期或已被吊销");

        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return WriteProblemAsync(StatusCodes.Status401Unauthorized, "需要有效的访问令牌");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        => WriteProblemAsync(StatusCodes.Status403Forbidden, "访问令牌没有执行该操作的权限");

    private Task WriteProblemAsync(int status, string title)
        => Results.Problem(statusCode: status, title: title).ExecuteAsync(Context);
}
