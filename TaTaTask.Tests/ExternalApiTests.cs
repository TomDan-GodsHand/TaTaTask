using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using TaTaTask.Data;
using TaTaTask.External;
using TaTaTask.Models.Dtos;
using TaTaTask.Models.Entities;
using TaTaTask.Models.Enums;
using Xunit;

namespace TaTaTask.Tests;

public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "tatatask-tests-" + Guid.NewGuid().ToString("N"));
    public string Writer { get; private set; } = "";
    public string Reader { get; private set; } = "";
    public string Other { get; private set; } = "";
    public int OwnerId { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_directory);
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole().SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureServices(services => services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(_directory)));
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = $"Data Source={Path.Combine(_directory, "test.db")}",
            ["ReverseProxy"] = "true",
            ["Logging:LogLevel:Default"] = "Warning",
            ["ExternalAccess:AllowedOrigins:0"] = "https://portal.example",
        }));
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = new User { Username = "external-owner", PasswordHash = "unused", TimeZoneId = "Asia/Shanghai" };
        var other = new User { Username = "external-other", PasswordHash = "unused", TimeZoneId = "UTC" };
        db.Users.AddRange(owner, other);
        await db.SaveChangesAsync();
        OwnerId = owner.Id;
        var store = scope.ServiceProvider.GetRequiredService<ApiTokenStore>();
        Writer = await store.CreateAsync(owner.Id, "writer", true, 1);
        Reader = await store.CreateAsync(owner.Id, "reader", false, 1);
        Other = await store.CreateAsync(other.Id, "other", true, 1);
    }

    public HttpClient Client(string? token = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false,
        });
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient CookieClient()
    {
        using var scope = Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, OwnerId.ToString()), new Claim(ClaimTypes.Name, "external-owner"),
        ], CookieAuthenticationDefaults.AuthenticationScheme));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1),
        }, CookieAuthenticationDefaults.AuthenticationScheme);
        var client = Client();
        client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
        return client;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
            Directory.Delete(_directory);
        }
    }
}

public sealed class ExternalApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static async Task<TodoItemDto> Create(HttpClient client, string title, params string[] steps)
    {
        var response = await client.PostAsJsonAsync("/api/v1/tasks", new CreateTodoRequest { Title = title, Steps = steps.ToList() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TodoItemDto>())!;
    }

    [Theory]
    [InlineData("/api/v1/tasks")]
    [InlineData("/mcp")]
    [InlineData("/openapi/external.json")]
    public async Task Anonymous_gets_401_without_html_or_login_redirect(string path)
    {
        using var client = fixture.Client();
        var response = path == "/mcp"
            ? await client.PostAsJsonAsync(path, new { jsonrpc = "2.0", id = 1, method = "tools/list" })
            : await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Cookie_cannot_authorize_external_api_and_token_cannot_authorize_account_api()
    {
        using var cookie = fixture.CookieClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await cookie.GetAsync("/api/v1/tasks")).StatusCode);
        using var token = fixture.Client(fixture.Writer);
        var response = await token.GetAsync("/api/user/settings");
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Tokens_are_hashed_and_revocation_and_expiration_take_effect_on_next_request()
    {
        using var scope = fixture.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ApiTokenStore>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var raw = await store.CreateAsync(fixture.OwnerId, "revoke", true, 1);
        var record = await db.ApiTokens.AsNoTracking().SingleAsync(t => t.TokenHash == ExternalAuth.Hash(raw));
        Assert.NotEqual(raw, record.TokenHash);
        Assert.Equal(64, record.TokenHash.Length);
        using var client = fixture.Client(raw);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode);
        await store.RevokeAsync(fixture.OwnerId + 100000, record.Id);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode);
        await store.RevokeAsync(fixture.OwnerId, record.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);

        raw = await store.CreateAsync(fixture.OwnerId, "expire", false, 1);
        await db.ApiTokens.Where(t => t.TokenHash == ExternalAuth.Hash(raw))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        using var expired = fixture.Client(raw);
        Assert.Equal(HttpStatusCode.Unauthorized, (await expired.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Read_only_token_can_query_but_cannot_write()
    {
        using var client = fixture.Client(fixture.Reader);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/tasks", new { title = "forbidden" })).StatusCode);
    }

    [Fact]
    public async Task Task_crud_is_isolated_by_owner_and_preserves_local_wall_clock()
    {
        using var writer = fixture.Client(fixture.Writer);
        using var other = fixture.Client(fixture.Other);
        var task = await Create(writer, "CRUD");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/v1/tasks/{task.Id}")).StatusCode);
        var localDue = new DateTime(2027, 1, 2, 18, 30, 0, DateTimeKind.Unspecified);
        var response = await writer.PutAsJsonAsync($"/api/v1/tasks/{task.Id}", new UpdateTodoRequest
        {
            Title = "updated", Priority = 3, DueDate = localDue,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<TodoItemDto>())!;
        Assert.Equal(localDue, updated.DueDate);
        Assert.Equal(DateTimeKind.Unspecified, updated.DueDate!.Value.Kind);
        using var scope = fixture.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().TodoItems.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal(new DateTime(2027, 1, 2, 10, 30, 0), stored.DueDate);
        Assert.Equal(HttpStatusCode.NoContent, (await writer.DeleteAsync($"/api/v1/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await writer.GetAsync($"/api/v1/tasks/{task.Id}")).StatusCode);
    }

    [Fact]
    public async Task Pagination_filters_and_exact_tag_matching_are_applied_before_paging()
    {
        using var client = fixture.Client(fixture.Writer);
        var prefix = Guid.NewGuid().ToString("N");
        foreach (var tag in new[] { prefix, prefix, prefix + "suffix" })
        {
            var response = await client.PostAsJsonAsync("/api/v1/tasks", new { title = prefix, tags = tag, priority = 2 });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        var result = (await client.GetFromJsonAsync<PagedResult<TodoItemDto>>($"/api/v1/tasks?tag={prefix}&pageSize=1&sort=created"))!;
        var next = (await client.GetFromJsonAsync<PagedResult<TodoItemDto>>($"/api/v1/tasks?tag={prefix}&pageSize=1&page=2&sort=created"))!;
        Assert.Equal(2, result.Total);
        Assert.Single(result.Items);
        Assert.Equal("Asia/Shanghai", result.TimeZoneId);
        Assert.NotEqual(result.Items[0].Id, next.Items[0].Id);
        Assert.Empty((await client.GetFromJsonAsync<PagedResult<TodoItemDto>>($"/api/v1/tasks?tag={prefix}&page=3&pageSize=1"))!.Items);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("status=999")]
    [InlineData("sort=anything")]
    [InlineData("priority=-1")]
    public async Task Invalid_query_returns_problem_details(string query)
    {
        using var client = fixture.Client(fixture.Reader);
        var response = await client.GetAsync("/api/v1/tasks?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Completion_is_idempotent_and_status_gate_and_unfreeze_are_preserved()
    {
        using var client = fixture.Client(fixture.Writer);
        var task = await Create(client, "steps", "first");
        var statusUrl = $"/api/v1/tasks/{task.Id}/status";
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(statusUrl, new { status = 4 })).StatusCode);
        var url = $"/api/v1/tasks/{task.Id}/steps/{task.Steps[0].Id}/completion";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(url, new { })).StatusCode);
        for (var i = 0; i < 2; i++)
        {
            var response = await client.PutAsJsonAsync(url, new { completed = true });
            var updated = (await response.Content.ReadFromJsonAsync<TodoItemDto>())!;
            Assert.True(updated.Steps[0].IsCompleted);
            Assert.Equal(TodoStatus.InProgress, updated.Status);
        }
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(statusUrl, new { status = 5, frozenReason = "waiting" })).StatusCode);
        var unfrozen = await client.PostAsync($"/api/v1/tasks/{task.Id}/unfreeze", null);
        Assert.Equal(TodoStatus.InProgress, (await unfrozen.Content.ReadFromJsonAsync<TodoItemDto>())!.Status);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(statusUrl, new { status = 4 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/tasks/{task.Id}/archive", null)).StatusCode);
    }

    [Fact]
    public async Task Invalid_creation_does_not_persist_and_UTC_input_is_rejected()
    {
        using var client = fixture.Client(fixture.Writer);
        foreach (var input in new object[]
        {
            new { title = " " }, new { title = "bad", priority = 9 }, new { title = "bad", dueDate = "2027-01-02T00:00:00Z" },
            new { title = "bad", status = 4, steps = new[] { "incomplete" } },
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/tasks", input)).StatusCode);
    }

    [Fact]
    public async Task Schedule_rules_crud_and_today_materialization_work_without_duplicates()
    {
        using var client = fixture.Client(fixture.Writer);
        var request = new SaveScheduleRuleRequest
        {
            Title = "daily", DaysOfWeek = 127, Mode = ScheduleRuleMode.Generate,
            StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0), Steps = ["one"],
        };
        var response = await client.PostAsJsonAsync("/api/v1/schedule/rules", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var rule = (await response.Content.ReadFromJsonAsync<ScheduleRuleDto>())!;
        using var other = fixture.Client(fixture.Other);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/schedule/rules/{rule.Id}")).StatusCode);
        request.Title = "updated daily";
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/schedule/rules/{rule.Id}", request)).StatusCode);
        var first = (await client.GetFromJsonAsync<TodayScheduleDto>("/api/v1/schedule/today"))!;
        var second = (await client.GetFromJsonAsync<TodayScheduleDto>("/api/v1/schedule/today"))!;
        Assert.Single(first.Entries, e => e.RuleId == rule.Id);
        Assert.Equal(JsonSerializer.Serialize(first.Entries), JsonSerializer.Serialize(second.Entries));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/schedule/rules/{rule.Id}")).StatusCode);
    }

    [Fact]
    public async Task Cors_allows_only_configured_origin_and_MCP_rejects_untrusted_origin()
    {
        using var client = fixture.Client(fixture.Writer);
        foreach (var origin in new[] { "https://portal.example", "https://untrusted.example" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/tasks");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
            var response = await client.SendAsync(request);
            Assert.Equal(origin == "https://portal.example", response.Headers.Contains("Access-Control-Allow-Origin"));
        }
        using var mcpRequest = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        mcpRequest.Headers.Add("Origin", "https://untrusted.example");
        mcpRequest.Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(mcpRequest)).StatusCode);
    }

    [Fact]
    public async Task Openapi_contains_only_external_routes_and_Bearer_security()
    {
        using var client = fixture.Client(fixture.Reader);
        var json = await client.GetStringAsync("/openapi/external.json");
        using var document = JsonDocument.Parse(json);
        Assert.All(document.RootElement.GetProperty("paths").EnumerateObject(), p => Assert.StartsWith("/api/v1/", p.Name));
        Assert.True(document.RootElement.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));
    }

    [Fact]
    public async Task External_write_notifies_the_owners_connected_browser()
    {
        using var browser = fixture.CookieClient();
        var cookie = browser.DefaultRequestHeaders.GetValues("Cookie").Single();
        await using var connection = new HubConnectionBuilder().WithUrl("https://localhost/hubs/todo", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => fixture.Server.CreateHandler();
            options.Headers["Cookie"] = cookie;
        }).Build();
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On("Refresh", () => refreshed.TrySetResult());
        await connection.StartAsync();
        using var writer = fixture.Client(fixture.Writer);
        await Create(writer, "external notification");
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Task_queries_lazily_archive_old_completed_tasks_and_can_include_all_archive_states()
    {
        using var client = fixture.Client(fixture.Writer);
        var title = Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync("/api/v1/tasks", new { title, status = 4 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = (await response.Content.ReadFromJsonAsync<TodoItemDto>())!;
        Assert.NotNull(task.DoneAt);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.TodoItems.Where(t => t.Id == task.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.DoneAt, DateTime.UtcNow.AddDays(-8)));
        }
        using var reader = fixture.Client(fixture.Reader);
        var archived = (await reader.GetFromJsonAsync<PagedResult<TodoItemDto>>($"/api/v1/tasks?archived=true&q={title}"))!;
        Assert.True(Assert.Single(archived.Items).IsArchived);
        var all = (await reader.GetFromJsonAsync<PagedResult<TodoItemDto>>($"/api/v1/tasks?archived=&q={title}"))!;
        Assert.Equal(1, all.Total);
    }

    [Fact]
    public async Task Integrations_page_requires_cookie_and_renders_without_secret_hashes()
    {
        using var client = fixture.CookieClient();
        var response = await client.GetAsync("/integrations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("/mcp", html);
        Assert.Contains("writer", html);
        Assert.DoesNotContain(fixture.Writer, html);
        Assert.DoesNotContain(ExternalAuth.Hash(fixture.Writer), html);
    }

    private async Task<McpClient> Mcp(string token)
    {
        var http = fixture.Client(token);
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp,
        }, http);
        return await McpClient.CreateAsync(transport);
    }

    [Theory]
    [InlineData("2025-03-26")]
    [InlineData("2025-11-25")]
    public async Task MCP_accepts_initialize_from_clients_using_earlier_Streamable_HTTP_versions(string version)
    {
        using var client = fixture.Client(fixture.Reader);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        var response = await client.PostAsJsonAsync("/mcp", new
        {
            jsonrpc = "2.0", id = 1, method = "initialize",
            @params = new { protocolVersion = version, capabilities = new { }, clientInfo = new { name = "compatibility-test", version = "1.0" } },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = body.Split('\n').FirstOrDefault(line => line.StartsWith("data: "))?[6..] ?? body;
        using var document = JsonDocument.Parse(json);
        Assert.Equal(version, document.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public async Task Official_MCP_client_can_initialize_discover_and_call_tools_with_user_context()
    {
        await using var mcp = await Mcp(fixture.Writer);
        var tools = await mcp.ListToolsAsync();
        Assert.Contains(tools, t => t.Name == "list_tasks");
        Assert.Contains(tools, t => t.Name == "delete_task");
        Assert.DoesNotContain("service", tools.Single(t => t.Name == "create_task").JsonSchema.ToString());
        var created = await mcp.CallToolAsync("create_task", new Dictionary<string, object?>
        {
            ["request"] = new { title = "from MCP", steps = new[] { "one" } },
        });
        Assert.False(created.IsError == true);
        var id = created.StructuredContent!.Value.GetProperty("id").GetInt32();
        using var http = fixture.Client(fixture.Writer);
        Assert.Equal("from MCP", (await http.GetFromJsonAsync<TodoItemDto>($"/api/v1/tasks/{id}"))!.Title);
        var invalid = await mcp.CallToolAsync("list_tasks", new Dictionary<string, object?> { ["pageSize"] = 101 });
        Assert.True(invalid.IsError);
        Assert.Equal(400, invalid.StructuredContent!.Value.GetProperty("status").GetInt32());
        await using var other = await Mcp(fixture.Other);
        var hidden = await other.CallToolAsync("get_task", new Dictionary<string, object?> { ["id"] = id });
        Assert.True(hidden.IsError);
        Assert.Equal(404, hidden.StructuredContent!.Value.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task MCP_read_only_token_cannot_call_write_tools_and_invalid_token_is_rejected()
    {
        await using var mcp = await Mcp(fixture.Reader);
        var result = await mcp.CallToolAsync("create_task", new Dictionary<string, object?> { ["request"] = new { title = "forbidden MCP" } });
        Assert.True(result.IsError);
        Assert.Equal(403, result.StructuredContent!.Value.GetProperty("status").GetInt32());
        using var invalid = fixture.Client("ttt_" + new string('0', 64));
        Assert.Equal(HttpStatusCode.Unauthorized, (await invalid.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" })).StatusCode);
    }
}
