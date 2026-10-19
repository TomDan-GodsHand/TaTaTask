using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace TaTaTask.Api;

/// <summary>只信任直连应用的代理；公网客户端的 IP 协议栈由 EdgeOne/Caddy 处理。</summary>
public static class ProxySupport
{
    public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        options.ForwardLimit = 1;
        // 框架默认已信任回环地址；容器或内网部署需显式列出 Caddy 地址。
        foreach (var address in configuration.GetSection("ReverseProxyOptions:KnownProxies").Get<string[]>() ?? [])
            options.KnownProxies.Add(IPAddress.Parse(address));
        foreach (var network in configuration.GetSection("ReverseProxyOptions:KnownNetworks").Get<string[]>() ?? [])
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
}
