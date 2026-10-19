using Microsoft.EntityFrameworkCore;
using TaTaTask.Data;
using TaTaTask.Models.Dtos.Api;
using TaTaTask.Services;

namespace TaTaTask.Api;

/// <summary>命令行令牌管理；创建成功时 stdout 只输出一次明文，错误写 stderr 并返回非零退出码。</summary>
public static class ApiTokenCli
{
    public static bool IsRequested(string[] args)
        => args.Contains("--create-api-token") || args.Contains("--list-api-tokens");

    // 令牌参数由本类校验，不能让宿主配置解析器先因缺参而抛出未处理异常。
    public static string[] HostArguments(string[] args)
    {
        if (!IsRequested(args)) return args;
        string[] flags = ["--create-api-token", "--list-api-tokens", "--token-name", "--token-scope", "--token-days"];
        var result = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!flags.Contains(args[i])) { result.Add(args[i]); continue; }
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) i++;
        }
        return result.ToArray();
    }

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            var create = Array.IndexOf(args, "--create-api-token");
            var list = Array.IndexOf(args, "--list-api-tokens");
            if ((create >= 0) == (list >= 0))
                throw ApiException.BadRequest("请指定 --create-api-token 或 --list-api-tokens 其中一个命令");

            string? Option(string name)
            {
                var index = Array.IndexOf(args, name);
                if (index < 0) return null;
                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                    throw ApiException.BadRequest($"{name} 缺少参数");
                return args[index + 1];
            }

            var username = Option(create >= 0 ? "--create-api-token" : "--list-api-tokens");
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tokens = scope.ServiceProvider.GetRequiredService<ApiTokenService>();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username)
                ?? throw ApiException.NotFound("用户不存在");
            var tz = UserTime.Resolve(user.TimeZoneId);
            if (list >= 0)
            {
                foreach (var token in await tokens.ListAsync(user.Id, tz))
                    await output.WriteLineAsync($"#{token.Id}\t{token.Name}\t{token.Prefix}…\t"
                        + (token.CanWrite ? "读写" : "只读")
                        + $"\t最后使用 {token.LastUsedAt?.ToString("yyyy-MM-dd HH:mm") ?? "从未"}"
                        + $"\t过期 {token.ExpiresAt?.ToString("yyyy-MM-dd HH:mm") ?? "永不"}"
                        + $"\t{(token.IsActive ? "有效" : "已失效")}");
                return 0;
            }

            var scopeName = Option("--token-scope") ?? "read";
            if (scopeName is not ("read" or "write"))
                throw ApiException.BadRequest("--token-scope 需为 read 或 write");
            var daysText = Option("--token-days");
            var days = 0;
            if (daysText is not null && (!int.TryParse(daysText, out days) || days is < 0 or > 365))
                throw ApiException.BadRequest("--token-days 需为 0–365，0 表示永不过期");
            var result = await tokens.CreateAsync(user.Id, new CreateApiTokenRequest
            {
                Name = Option("--token-name") ?? "cli",
                CanWrite = scopeName == "write",
                ExpiresAt = days == 0 ? null : DateTimeOffset.UtcNow.AddDays(days),
            }, tz);
            await output.WriteLineAsync(result.Secret);
            return 0;
        }
        catch (ApiException ex)
        {
            await error.WriteLineAsync(ex.Message);
            return 2;
        }
    }
}
