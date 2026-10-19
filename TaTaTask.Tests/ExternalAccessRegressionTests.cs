using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaTaTask.Api;
using TaTaTask.Data;
using TaTaTask.Hubs;
using TaTaTask.Models.Dtos.Api;
using TaTaTask.Models.Entities;
using TaTaTask.Models.Enums;
using TaTaTask.Services;
using Xunit;

namespace TaTaTask.Tests;

public sealed partial class ExternalApiTests
{
    [Fact]
    public async Task All_external_write_routes_reject_read_tokens_and_cookies_before_model_binding()
    {
        using var reader = fixture.Client(fixture.Reader);
        using var cookie = fixture.CookieClient();
        var endpoints = fixture.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("api/v1/", StringComparison.Ordinal) == true).ToList();
        var checkedRoutes = 0;
        foreach (var endpoint in endpoints)
        {
            var methods = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? [];
            foreach (var method in methods.Where(m => m is "POST" or "PUT" or "PATCH" or "DELETE"))
            {
                var path = "/" + System.Text.RegularExpressions.Regex.Replace(endpoint.RoutePattern.RawText!, "\\{[^}]+\\}", "1");
                using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) };
                Assert.Equal(HttpStatusCode.Forbidden, (await reader.SendAsync(request)).StatusCode);
                using var cookieRequest = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) };
                Assert.Equal(HttpStatusCode.Unauthorized, (await cookie.SendAsync(cookieRequest)).StatusCode);
                checkedRoutes++;
            }
        }
        Assert.True(checkedRoutes >= 25, $"Only checked {checkedRoutes} write routes");
    }

    [Fact]
    public async Task Skip_carry_freeze_and_delete_schedule_actions_keep_existing_business_behavior()
    {
        using var client = fixture.Client(fixture.Writer);
        var rule = (await (await client.PostAsJsonAsync("/api/v1/schedule/rules", new
        {
            title = "skip-check", mode = "generate", daysOfWeek = 127,
        })).Content.ReadFromJsonAsync<ScheduleRuleApiDto>())!;
        var initial = (await client.GetFromJsonAsync<TodayScheduleApiDto>("/api/v1/schedule/today"))!;
        var generated = Assert.Single(initial.Entries, e => e.RuleId == rule.Id);
        var skipped = await client.PostAsync($"/api/v1/schedule/rules/{rule.Id}/skip-today", null);
        Assert.Equal("skipped", Assert.Single((await skipped.Content.ReadFromJsonAsync<TodayScheduleApiDto>())!.Entries, e => e.RuleId == rule.Id).State);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/tasks/{generated.TaskId}")).StatusCode);

        foreach (var action in new[] { "carry-over", "freeze", "task" })
        {
            var title = "unfinished-" + Guid.NewGuid().ToString("N");
            var entry = Assert.Single((await (await client.PostAsJsonAsync("/api/v1/schedule/entries", new { title }))
                .Content.ReadFromJsonAsync<TodayScheduleApiDto>())!.Entries, e => e.Title == title);
            using (var scope = fixture.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().ScheduleEntries.Where(e => e.Id == entry.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(e => e.Date, initial.Date.AddDays(-1)));
            var response = action == "task" ? await client.DeleteAsync($"/api/v1/schedule/entries/{entry.Id}/task")
                : await client.PostAsync($"/api/v1/schedule/entries/{entry.Id}/{action}", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var today = (await response.Content.ReadFromJsonAsync<TodayScheduleApiDto>())!;
            Assert.DoesNotContain(today.Unfinished, e => e.Id == entry.Id);
            if (action == "carry-over") Assert.Contains(today.Entries, e => e.TaskId == entry.TaskId);
            else if (action == "freeze") Assert.Equal("frozen", (await client.GetFromJsonAsync<TaskDto>($"/api/v1/tasks/{entry.TaskId}"))!.Status);
            else Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/tasks/{entry.TaskId}")).StatusCode);
        }
    }

    [Fact]
    public async Task Tags_and_statistics_only_include_the_token_owner_and_are_not_cacheable()
    {
        var tag = Guid.NewGuid().ToString("N");
        using var owner = fixture.Client(fixture.Writer);
        using var other = fixture.Client(fixture.Other);
        await owner.PostAsJsonAsync("/api/v1/tasks", new { title = "tag-stats", tags = new[] { tag } });
        await other.PostAsJsonAsync("/api/v1/tasks", new { title = "other-stats", tags = new[] { tag } });
        var response = await owner.GetAsync("/api/v1/tags");
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(1, Assert.Single((await response.Content.ReadFromJsonAsync<List<TagCountApiDto>>())!, t => t.Tag == tag).Count);
        var stats = await owner.GetAsync("/api/v1/stats");
        Assert.Equal(HttpStatusCode.OK, stats.StatusCode);
        Assert.True(stats.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("192.0.2.10", false)]
    [InlineData("192.0.2.10", true)]
    [InlineData("fd00::10", true)]
    [InlineData("172.20.0.10", true)]
    public async Task Caddy_forwarded_https_and_public_host_are_used_only_from_trusted_proxies(string remote, bool trusted)
    {
        using var app = fixture.WithWebHostBuilder(builder =>
        {
            if (trusted && !IPAddress.IsLoopback(IPAddress.Parse(remote)))
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [remote.StartsWith("172.20.", StringComparison.Ordinal)
                        ? "ReverseProxyOptions:KnownNetworks:0" : "ReverseProxyOptions:KnownProxies:0"]
                        = remote.StartsWith("172.20.", StringComparison.Ordinal) ? "172.20.0.0/24" : remote,
                }));
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new ProxyConnection(remote)));
        });
        using var client = app.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Reader);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "tasks.example.com");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.8, 2001:db8::5");
        client.DefaultRequestHeaders.Add("Origin", "https://tasks.example.com");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        var response = await client.PostAsJsonAsync("/mcp", new
        {
            jsonrpc = "2.0", id = 1, method = "initialize",
            @params = new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "proxy-test", version = "1" } },
        });
        Assert.True((trusted ? HttpStatusCode.OK : HttpStatusCode.Forbidden) == response.StatusCode,
            $"{response.StatusCode}; {response.Headers}; {await response.Content.ReadAsStringAsync()}");
        Assert.Equal(trusted ? "https" : "http", response.Headers.GetValues("X-Test-Scheme").Single());
        Assert.Equal(trusted ? "tasks.example.com" : "localhost", response.Headers.GetValues("X-Test-Host").Single());
        Assert.Equal(trusted ? "2001:db8::5" : remote, response.Headers.GetValues("X-Test-Remote").Single());
        if (trusted) Assert.True(response.Headers.CacheControl?.NoStore);
    }

    private sealed class ProxyConnection(string address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, proceed) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(address);
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers["X-Test-Scheme"] = context.Request.Scheme;
                    context.Response.Headers["X-Test-Host"] = context.Request.Host.Value;
                    context.Response.Headers["X-Test-Remote"] = context.Connection.RemoteIpAddress?.ToString();
                    return Task.CompletedTask;
                });
                await proceed(context);
            });
            next(app);
        };
    }

    [Theory]
    [InlineData("/api/todos")]
    [InlineData("/api/schedule/today")]
    [InlineData("/api/user/settings")]
    [InlineData("/integrations")]
    [InlineData("/settings")]
    [InlineData("/hubs/todo/negotiate?negotiateVersion=1")]
    public async Task Bearer_cannot_enter_any_cookie_only_surface(string path)
    {
        foreach (var token in new[] { fixture.Reader, fixture.Writer })
        {
            using var client = fixture.Client(token);
            var response = path.Contains("negotiate") ? await client.PostAsync(path, null) : await client.GetAsync(path);
            Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect, $"{path}: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task Read_token_cannot_bypass_scope_via_legacy_task_creation()
    {
        using var reader = fixture.Client(fixture.Reader);
        var title = Guid.NewGuid().ToString("N");
        var response = await reader.PostAsJsonAsync("/api/todos", new { title });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var scope = fixture.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().TodoItems.AnyAsync(t => t.Title == title));
    }

    [Fact]
    public async Task Never_expiring_tokens_record_last_use_without_granting_admin_identity()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ApiTokenService>();
        var created = await tokens.CreateAsync(fixture.OwnerId, new CreateApiTokenRequest { Name = "permanent" }, TimeZoneInfo.Utc);
        Assert.Null(created.Token.ExpiresAt);
        Assert.Null(created.Token.LastUsedAt);
        using var client = fixture.Client(created.Secret);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode);
        var record = await db.ApiTokens.AsNoTracking().SingleAsync(t => t.Id == created.Token.Id);
        Assert.NotNull(record.LastUsedAt);
        var principal = await tokens.AuthenticateAsync(created.Secret, default);
        Assert.False(principal!.HasClaim(c => c.Type == "IsAdmin"));
        Assert.False(principal.HasClaim(ApiTokenDefaults.ScopeClaim, "write"));
        await tokens.RevokeAsync(fixture.OwnerId, record.Id);
    }

    [Theory]
    [InlineData("--token-days", "garbage")]
    [InlineData("--token-days", "-1")]
    [InlineData("--token-days", "366")]
    [InlineData("--token-scope", "admin")]
    public async Task Token_cli_rejects_invalid_options_without_creating_a_token(string option, string value)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.ApiTokens.CountAsync();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await ApiTokenCli.RunAsync(fixture.Services, ["--create-api-token", "external-owner", option, value], output, error);
        Assert.Equal(2, code);
        Assert.Equal("", output.ToString());
        Assert.NotEmpty(error.ToString());
        Assert.Equal(before, await db.ApiTokens.CountAsync());
    }

    [Fact]
    public async Task Token_cli_creates_usable_secret_and_lists_only_metadata()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, await ApiTokenCli.RunAsync(fixture.Services,
            ["--create-api-token", "external-owner", "--token-name", "cli-check", "--token-scope", "write", "--token-days", "1"], output, error));
        var secret = output.ToString().Trim();
        Assert.StartsWith("ttt_", secret);
        Assert.DoesNotContain('\n', secret);
        using var client = fixture.Client(secret);
        Assert.True((await client.GetFromJsonAsync<MeDto>("/api/v1/me"))!.CanWrite);
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await ApiTokenCli.RunAsync(fixture.Services, ["--list-api-tokens", "external-owner"], output, error));
        Assert.Contains("cli-check", output.ToString());
        Assert.DoesNotContain(secret, output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Original_token_migration_upgrades_without_losing_tokens()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestResults", $"upgrade-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261019023059_AddApiTokens");
            var user = new User { Username = "upgrade", PasswordHash = "unused", TimeZoneId = "UTC" };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            var secret = "ttt_" + Guid.NewGuid().ToString("N");
            var hash = ApiTokenService.Hash(secret);
            var now = DateTime.UtcNow;
            var expires = now.AddDays(1);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ApiTokens (UserId,Name,TokenHash,Prefix,CanWrite,CreatedAt,ExpiresAt) VALUES ({user.Id},{"original"},{hash},{secret.Substring(0, 12)},{true},{now},{expires})");
            await migrator.MigrateAsync();
            var tokens = new ApiTokenService(db);
            var principal = await tokens.AuthenticateAsync(secret, default);
            Assert.NotNull(principal);
            Assert.True(principal.HasClaim(ApiTokenDefaults.ScopeClaim, "write"));
            Assert.Equal(hash, (await db.ApiTokens.AsNoTracking().SingleAsync()).TokenHash);
            var permanent = await tokens.CreateAsync(user.Id, new CreateApiTokenRequest { Name = "new" }, TimeZoneInfo.Utc);
            Assert.Null(permanent.Token.ExpiresAt);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task Patch_preserves_fields_and_invalid_steps_do_not_persist_partial_changes()
    {
        using var client = fixture.Client(fixture.Writer);
        var response = await client.PostAsJsonAsync("/api/v1/tasks", new
        {
            title = "original", description = "keep", tags = new[] { "patch" }, priority = 3, steps = new[] { "a", "b" },
        });
        var task = (await response.Content.ReadFromJsonAsync<TaskDto>())!;
        var url = $"/api/v1/tasks/{task.Id}";
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync(url, new { title = "patched" })).StatusCode);
        var updated = (await client.GetFromJsonAsync<TaskDto>(url))!;
        Assert.Equal("keep", updated.Description);
        Assert.Equal(3, updated.Priority);
        Assert.Equal(new[] { "patch" }, updated.Tags);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync(url, new { title = "should roll back", stepIdsToDelete = new[] { int.MaxValue } })).StatusCode);
        Assert.Equal("patched", (await client.GetFromJsonAsync<TaskDto>(url))!.Title);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(url + "/steps/order", new { stepIds = new[] { task.Steps[0].Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync(url, new
        {
            stepIdsToDelete = new[] { task.Steps[0].Id }, stepsOrder = task.Steps.Select(s => s.Id).ToArray(),
        })).StatusCode);
        Assert.Equal(2, (await client.GetFromJsonAsync<TaskDto>(url))!.Steps.Count);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(url + "/steps/order", new { stepIds = task.Steps.Select(s => s.Id).Reverse() })).StatusCode);
        Assert.Equal(task.Steps.Last().Id, (await client.GetFromJsonAsync<TaskDto>(url))!.Steps.First().Id);
    }

    [Fact]
    public async Task Offset_input_preserves_the_exact_instant_during_dst_overlap()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User { Username = "dst-" + Guid.NewGuid().ToString("N"), PasswordHash = "unused", TimeZoneId = "America/New_York" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var secret = (await scope.ServiceProvider.GetRequiredService<ApiTokenService>().CreateAsync(user.Id,
            new CreateApiTokenRequest { Name = "dst", CanWrite = true }, TimeZoneInfo.Utc)).Secret;
        using var client = fixture.Client(secret);
        foreach (var offset in new[] { "-04:00", "-05:00" })
        {
            var due = DateTimeOffset.Parse("2026-11-01T01:30:00" + offset);
            var response = await client.PostAsJsonAsync("/api/v1/tasks", new { title = "overlap", dueDate = due });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var task = (await response.Content.ReadFromJsonAsync<TaskDto>())!;
            Assert.Equal(due, task.DueDate);
            Assert.Equal(due.Offset, task.DueDate!.Value.Offset);
            Assert.Equal(due.UtcDateTime, (await db.TodoItems.AsNoTracking().SingleAsync(t => t.Id == task.Id)).DueDate);
        }
    }

    [Theory]
    [InlineData("garbage", "10:00")]
    [InlineData("9:00", "10:00")]
    [InlineData("09:00", "08:00")]
    public async Task Invalid_schedule_time_is_rejected_without_creating_a_rule(string start, string end)
    {
        using var client = fixture.Client(fixture.Writer);
        var title = Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/schedule/rules", new
        {
            title, mode = "generate", daysOfWeek = 127, startTime = start, endTime = end,
        })).StatusCode);
        using var scope = fixture.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().ScheduleRules.AnyAsync(r => r.Title == title));
    }

    [Fact]
    public async Task Schedule_patch_preserves_template_and_generate_steps_complete_the_task()
    {
        using var client = fixture.Client(fixture.Writer);
        var response = await client.PostAsJsonAsync("/api/v1/schedule/rules", new
        {
            title = "generate-chain", mode = "generate", daysOfWeek = 127, startTime = "06:30", endTime = "07:30", steps = new[] { "one", "two" },
        });
        var rule = (await response.Content.ReadFromJsonAsync<ScheduleRuleApiDto>())!;
        var patched = await client.PatchAsJsonAsync($"/api/v1/schedule/rules/{rule.Id}", new { title = "renamed" });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        var saved = (await patched.Content.ReadFromJsonAsync<ScheduleRuleApiDto>())!;
        Assert.Equal("generate", saved.Mode);
        Assert.Equal("06:30", saved.StartTime);
        Assert.Equal(2, saved.Steps.Count);
        var today = (await client.GetFromJsonAsync<TodayScheduleApiDto>("/api/v1/schedule/today"))!;
        var entry = Assert.Single(today.Entries, e => e.RuleId == rule.Id);
        foreach (var step in entry.TaskSteps)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/schedule/entries/{entry.Id}/steps/{step.Id}/toggle", null)).StatusCode);
        var task = (await client.GetFromJsonAsync<TaskDto>($"/api/v1/tasks/{entry.TaskId}"))!;
        Assert.Equal("done", task.Status);
        today = (await client.GetFromJsonAsync<TodayScheduleApiDto>("/api/v1/schedule/today"))!;
        Assert.Equal("completed", Assert.Single(today.Entries, e => e.RuleId == rule.Id).State);
        var history = (await client.GetFromJsonAsync<PagedResult<ScheduleHistoryApiDto>>($"/api/v1/schedule/history?ruleId={rule.Id}"))!;
        Assert.Contains(history.Items.SelectMany(d => d.Entries), e => e.Id == entry.Id && e.State == "completed");
    }

    [Fact]
    public async Task Pick_uses_exact_tags_and_only_scheduled_steps_advance_today()
    {
        using var client = fixture.Client(fixture.Writer);
        var tag = Guid.NewGuid().ToString("N");
        var task = (await (await client.PostAsJsonAsync("/api/v1/tasks", new { title = "pick", tags = new[] { tag }, steps = new[] { "today", "later" } })).Content.ReadFromJsonAsync<TaskDto>())!;
        await client.PostAsJsonAsync("/api/v1/tasks", new { title = "wrong tag", tags = new[] { tag + "suffix" } });
        var rule = (await (await client.PostAsJsonAsync("/api/v1/schedule/rules", new { title = "pick", daysOfWeek = 127, tags = new[] { tag } })).Content.ReadFromJsonAsync<ScheduleRuleApiDto>())!;
        var today = (await client.GetFromJsonAsync<TodayScheduleApiDto>("/api/v1/schedule/today"))!;
        var entry = Assert.Single(today.Entries, e => e.RuleId == rule.Id);
        var candidates = (await client.GetFromJsonAsync<List<ScheduleCandidateApiDto>>($"/api/v1/schedule/entries/{entry.Id}/candidates"))!;
        Assert.Equal(task.Id, Assert.Single(candidates).Id);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/schedule/entries/{entry.Id}/pick", new { todoItemId = task.Id, stepIds = new[] { task.Steps[0].Id } })).StatusCode);
        var completed = await client.PostAsync($"/api/v1/schedule/entries/{entry.Id}/steps/{task.Steps[0].Id}/toggle", null);
        today = (await completed.Content.ReadFromJsonAsync<TodayScheduleApiDto>())!;
        Assert.Equal("completed", Assert.Single(today.Entries, e => e.Id == entry.Id).State);
        Assert.Equal("inProgress", (await client.GetFromJsonAsync<TaskDto>($"/api/v1/tasks/{task.Id}"))!.Status);
    }

    [Fact]
    public async Task Manual_entry_patch_retains_times_tags_and_deletion_removes_its_generated_task()
    {
        using var client = fixture.Client(fixture.Writer);
        var title = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync("/api/v1/schedule/entries", new { title, startTime = "14:30", endTime = "15:45", tags = new[] { "manual" } });
        var entry = Assert.Single((await response.Content.ReadFromJsonAsync<TodayScheduleApiDto>())!.Entries, e => e.Title == title);
        var patch = await client.PatchAsJsonAsync($"/api/v1/schedule/entries/{entry.Id}", new { title = title + "new" });
        var updated = Assert.Single((await patch.Content.ReadFromJsonAsync<TodayScheduleApiDto>())!.Entries, e => e.Id == entry.Id);
        Assert.Equal("14:30", updated.StartTime);
        Assert.Equal("15:45", updated.EndTime);
        Assert.Equal(new[] { "manual" }, updated.Tags);
        using var other = fixture.Client(fixture.Other);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/v1/schedule/entries/{entry.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/v1/schedule/entries/{entry.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/tasks/{entry.TaskId}")).StatusCode);
    }

    [Fact]
    public async Task MCP_errors_are_protocol_errors_for_permission_validation_and_business_conflicts()
    {
        using var client = fixture.Client(fixture.Writer);
        var task = await Create(client, "mcp gate", "unfinished");
        await using var mcp = await Mcp(fixture.Writer);
        var tools = await mcp.ListToolsAsync();
        Assert.Equal(37, tools.Count);
        foreach (var name in new[] { "update_manual_entry", "delete_manual_entry", "delete_entry_task", "unfreeze_task", "reorder_task_steps", "get_stats", "list_tags" })
            Assert.Contains(tools, t => t.Name == name);
        var blocked = await mcp.CallToolAsync("set_task_status", new Dictionary<string, object?> { ["id"] = task.Id, ["status"] = "done" });
        Assert.True(blocked.IsError);
        Assert.Equal(409, blocked.StructuredContent!.Value.GetProperty("status").GetInt32());
        var bad = await mcp.CallToolAsync("create_schedule_rule", new Dictionary<string, object?>
        {
            ["title"] = "invalid", ["mode"] = "generate", ["daysOfWeek"] = 127, ["startTime"] = "garbage",
        });
        Assert.True(bad.IsError);
        Assert.Equal(400, bad.StructuredContent!.Value.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Transaction_rolls_back_intermediate_saves_and_notifies_only_after_commit()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, fixture.OwnerId.ToString()), new Claim(ApiTokenDefaults.ScopeClaim, "write"),
            }, ApiTokenDefaults.Scheme)),
        };
        try
        {
            var title = Guid.NewGuid().ToString("N");
            var proxy = new CommitObserver(fixture.Services, title);
            var executor = new ApiCommandExecutor(db, scope.ServiceProvider.GetRequiredService<ApiContext>(), proxy,
                NullLogger<ApiCommandExecutor>.Instance);
            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(async () =>
            {
                db.TodoItems.Add(new TodoItem { UserId = fixture.OwnerId, Title = title });
                await db.SaveChangesAsync();
                throw new InvalidOperationException("injected after SaveChanges");
            }));
            Assert.False(await db.TodoItems.AnyAsync(t => t.Title == title));
            Assert.Equal(0, proxy.Notifications);
            Assert.Empty(db.ChangeTracker.Entries());
            // 通知失败仍应返回成功；观察者在通知时用另一连接检查提交已完成。
            var result = await executor.ExecuteAsync(async () =>
            {
                db.TodoItems.Add(new TodoItem { UserId = fixture.OwnerId, Title = title });
                await db.SaveChangesAsync();
                return 42;
            });
            Assert.Equal(42, result);
            Assert.Equal(1, proxy.Notifications);
            Assert.True(proxy.WasCommitted);
            Assert.Equal($"user_{fixture.OwnerId}", proxy.GroupName);
        }
        finally { accessor.HttpContext = previous; }
    }

    private sealed class CommitObserver(IServiceProvider services, string title) : IHubContext<TodoHub>, IHubClients, IClientProxy
    {
        public int Notifications { get; private set; }
        public bool WasCommitted { get; private set; }
        public string? GroupName { get; private set; }
        IHubClients IHubContext<TodoHub>.Clients => this;
        IGroupManager IHubContext<TodoHub>.Groups => throw new NotSupportedException();
        public IClientProxy All => this;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => this;
        public IClientProxy Client(string connectionId) => this;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => this;
        public IClientProxy Group(string groupName) { GroupName = groupName; return this; }
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Group(groupName);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => this;
        public IClientProxy User(string userId) => this;
        public IClientProxy Users(IReadOnlyList<string> userIds) => this;
        public async Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Notifications++;
            using var scope = services.CreateScope();
            WasCommitted = await scope.ServiceProvider.GetRequiredService<AppDbContext>().TodoItems.AnyAsync(t => t.Title == title, cancellationToken);
            throw new IOException("injected notification failure");
        }
    }
}
