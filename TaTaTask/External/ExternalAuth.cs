using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaTaTask.Data;

namespace TaTaTask.External;

public static class ExternalAuth
{
    public const string Scheme = "ApiToken";
    public const string Read = "ExternalRead";
    public const string Write = "ExternalWrite";
    public const string Scope = "tatatask_scope";

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed class ApiTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, AppDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var raw = header[7..].Trim();
        if (!raw.StartsWith("ttt_", StringComparison.Ordinal) || raw.Length != 68)
            return AuthenticateResult.Fail("Invalid access token");

        var hash = ExternalAuth.Hash(raw);
        var token = await db.ApiTokens.AsNoTracking().Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, Context.RequestAborted);
        if (token is null || token.RevokedAt is not null || token.ExpiresAt <= DateTime.UtcNow)
            return AuthenticateResult.Fail("Invalid or expired access token");

        // No administrator claim: a token grants only the external API permissions selected by its owner.
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, token.UserId.ToString()),
            new Claim(ClaimTypes.Name, token.User.Username),
            new Claim(ExternalAuth.Scope, "read"),
            new Claim(ExternalAuth.Scope, token.CanWrite ? "write" : "read"),
        ], ExternalAuth.Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return Results.Problem(statusCode: 401, title: "需要有效的访问令牌").ExecuteAsync(Context);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        => Results.Problem(statusCode: 403, title: "访问令牌没有写入权限").ExecuteAsync(Context);
}
