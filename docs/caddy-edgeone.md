# 家庭 IPv6 + Caddy + EdgeOne 部署

适用链路：IPv4/IPv6 客户端 → EdgeOne → 家中公网 IPv6 → Caddy → 同机 TaTaTask。REST、MCP 和网页都使用同一个公网域名；应用与 Caddy 之间走 `127.0.0.1:5000`，不要求 .NET 直接接收公网 IPv6。

以下域名均为占位符：

| 用途 | 示例 | 配置 |
|---|---|---|
| 公网访问域名 | `tasks.example.com` | 接入 EdgeOne，按控制台分配的记录解析；客户端始终使用此域名 |
| 回源域名 | `home6.example.com` | AAAA 直接指向家中 IPv6；不要再次指回 EdgeOne，避免回源循环 |
| 应用监听 | `127.0.0.1:5000` | 仅供同机 Caddy 访问 |

## TaTaTask

将 `deploy/appsettings.Caddy.json` 复制为部署目录的 `appsettings.Production.json`，修改公网域名。这个示例是完整的生产环境配置文件；替换原文件时删除原有 `Kestrel:Endpoints:Https` 及证书配置，避免应用同时启动一个不需要的 HTTPS 端点。证书由 Caddy 管理。

```json
{
  "ReverseProxy": true,
  "AllowedHosts": "tasks.example.com;localhost;127.0.0.1",
  "ExternalAccess": { "AllowedOrigins": [] },
  "Kestrel": {
    "Endpoints": { "Http": { "Url": "http://127.0.0.1:5000" } }
  }
}
```

本模式禁用应用内 HTTPS 重定向；从受信代理恢复 `X-Forwarded-Proto`、`X-Forwarded-Host`，然后执行认证和 MCP Origin 校验。默认信任回环地址，且只处理最近一跳，正好对应同机 Caddy。不必把 EdgeOne 节点地址加入应用的受信代理：应用直连的是 Caddy。

未来改成容器或跨机器时，可以通过 `ReverseProxyOptions:KnownProxies`（IPv4/IPv6 数组）或 `ReverseProxyOptions:KnownNetworks`（CIDR 数组）添加直连代理。保持应用只对代理开放，不要清空受信列表来信任所有来源。

`ExternalAccess:AllowedOrigins` 是跨站网页的来源白名单，与回源域名、IPv4/IPv6 无关。同源访问及不带 Origin 的 agent 无需填写。第三方网页需要直连时填写其完整来源，如 `https://portal.example.com`。

## Caddy

修改并使用 `deploy/Caddyfile.example`：

```caddyfile
home6.example.com {
    reverse_proxy 127.0.0.1:5000 {
        header_up Host tasks.example.com
        header_up X-Forwarded-Host tasks.example.com
        header_up X-Forwarded-Proto https
        flush_interval -1
    }
}
```

该示例按回源 Host 为 `home6.example.com` 配置站点，并显式向应用还原公网访问域名。现有 Caddy 如已接收公网 Host，可以沿用现有站点，只需保证转发给应用的 Host、X-Forwarded-Host 为公网访问域名，X-Forwarded-Proto 为 `https`。

所有路径整体转发，不移除 `/mcp` 或 `/api/v1` 前缀，不改写 Authorization、Cookie、MCP-Protocol-Version、Mcp-Session-Id、Accept 等请求头。示例使用即时刷新以便转发 MCP 的 SSE 响应。网页的 Blazor `/_blazor` 和任务同步 `/hubs/todo` 也必须走同一代理。

## EdgeOne

在现有站点上核对以下回源与规则，具体选项名称以当前控制台为准：

1. 源站填写 `home6.example.com`，使用 IPv6 回源，HTTPS 端口与 Caddy 一致；回源 Host、TLS SNI 与 Caddy 的站点及证书一致。
2. 公网域名提供客户端需要的 IPv4/IPv6 接入。客户端使用 `https://tasks.example.com`，REST 为 `/api/v1`，MCP 为 `/mcp`。
3. 动态内容绕过缓存：`/api/*`、`/mcp*`、`/openapi/*`、`/_blazor*`、`/hubs/*`、登录与账号页面及登录后的 HTML。只缓存明确的静态资源，遵从源站 `Cache-Control: no-store`；不要用“缓存全部”规则覆盖它。
4. 转发 Authorization、Cookie 和 MCP 相关请求头，保留响应 Content-Type；允许 GET/POST/PUT/PATCH/DELETE/OPTIONS。MCP 和 API 请求不要被改成验证码、登录页或 JavaScript 验证页。
5. 对 `/_blazor*` 与 `/hubs/*` 启用 WebSocket 转发；为实时连接及 `/mcp` 配置足够的回源/空闲超时，避免响应缓冲。若套餐或规则会缓冲 SSE，需在边缘配置中解除该限制。

应用会给外部 API、MCP、OpenAPI、实时连接和已认证用户的响应发送 `Cache-Control: no-store`。这不能替代 EdgeOne 上强制缓存规则的检查。

## 验收

先在服务器上验证应用，再通过公网域名分别验证 IPv4/IPv6：

```bash
# 无令牌应为 401，不能是 HTML、301/302 跳转或边缘验证页
curl -i http://127.0.0.1:5000/api/v1/me
curl -4 -i https://tasks.example.com/api/v1/me
curl -6 -i https://tasks.example.com/api/v1/me

# 用网页创建的临时令牌验证，随后吊销
curl -4 -i -H "Authorization: Bearer $TATATASK_TOKEN" https://tasks.example.com/api/v1/me
curl -6 -i -H "Authorization: Bearer $TATATASK_TOKEN" https://tasks.example.com/api/v1/me
```

两次带令牌请求都应返回同一用户的 JSON 和 `Cache-Control: no-store`。只读令牌写入应为 403，吊销后应为 401。浏览器创建令牌时应显示完整明文；复制出的值应能通过上述请求。

再用 MCP 客户端连接公网 `/mcp`，完成 initialize、tools/list 和 list_tasks；浏览器同时打开两个标签页，外部写入后检查刷新，开发者工具检查 `/_blazor`、`/hubs/todo` 的连接。测试机本身无 IPv6 时，`curl -6` 失败不能说明源站或应用不支持 IPv6，需要换到有 IPv6 的网络。

仓库测试覆盖受信 IPv4/IPv6 回环代理、公网 Host/HTTPS 转发、MCP 同源校验和对伪造转发头的拒绝；真正的 EdgeOne 规则、证书、家庭防火墙和公网双栈连通性仍需部署后实测。
