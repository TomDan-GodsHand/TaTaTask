namespace TaTaTask.Api;

/// <summary>外部 API/MCP 的认证与授权常量。</summary>
public static class ApiTokenDefaults
{
    /// <summary>Bearer 令牌认证方案。</summary>
    public const string Scheme = "ApiToken";

    /// <summary>只读权限策略。</summary>
    public const string PolicyRead = "ExternalRead";

    /// <summary>读写权限策略。</summary>
    public const string PolicyWrite = "ExternalWrite";

    /// <summary>令牌权限声明类型（可有多条，值为 read / write）。</summary>
    public const string ScopeClaim = "tatatask_scope";

    /// <summary>令牌名声明类型。</summary>
    public const string TokenNameClaim = "tatatask_token_name";

    public const string ScopeRead = "read";
    public const string ScopeWrite = "write";

    /// <summary>令牌明文前缀。</summary>
    public const string TokenPrefix = "ttt_";
}
