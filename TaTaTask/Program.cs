using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using TaTaTask.Client.Services;
using TaTaTask.Components;
using TaTaTask.Components.Account;
using TaTaTask.Data;
using TaTaTask.Hubs;
using TaTaTask.Services;
using TaTaTask.Api;
using Microsoft.OpenApi;
using ModelContextProtocol.AspNetCore;

namespace TaTaTask
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(ApiTokenCli.HostArguments(args));
            if (ApiTokenCli.IsRequested(args))
                builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

            // Integrate with systemd (Type=notify lifetime + journal-formatted console logging
            // when running under systemd; no-op otherwise, e.g. on Windows/dev).
            builder.Host.UseSystemd();

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents()
                .AddInteractiveWebAssemblyComponents();

            builder.Services.AddMudServices();
            builder.Services.AddControllers();
            builder.Services.AddProblemDetails();
            builder.Services.AddOpenApi("v1", options =>
            {
                options.ShouldInclude = description => description.GroupName == "external";
                options.AddDocumentTransformer((document, context, cancellationToken) =>
                {
                    document.Info.Title = "TaTaTask External API";
                    document.Info.Version = "v1";
                    document.Info.Description = "使用个人访问令牌。外部日期时间使用带偏移的 ISO 8601；网页内部仍使用用户墙钟时间。";
                    document.Components ??= new();
                    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                    document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "TaTaTask personal access token",
                    };
                    document.Security ??= [];
                    document.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
                    });
                    return Task.CompletedTask;
                });
            });
            builder.Services.AddCascadingAuthenticationState();

            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

            builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();

            builder.Services.AddScoped<AuthenticationStateProvider, PersistingServerAuthenticationStateProvider>();

            builder.Services.Configure<ForwardedHeadersOptions>(options => ProxySupport.Configure(options, builder.Configuration));

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentUser, CurrentUser>();
            builder.Services.AddScoped<ServerTodoService>();
            builder.Services.AddScoped<ITodoService>(sp => sp.GetRequiredService<ServerTodoService>());
            builder.Services.AddScoped<IScheduleService, ServerScheduleService>();
            builder.Services.AddScoped<ApiTokenService>();
            builder.Services.AddScoped<ApiContext>();
            builder.Services.AddScoped<ApiCommandExecutor>();
            builder.Services.AddScoped<TaskApiService>();
            builder.Services.AddScoped<ScheduleApiService>();
            builder.Services.AddScoped<UserApiService>();
            builder.Services.AddMcpServer()
                .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
                .WithTools<TaTaTaskTools>();
            builder.Services.AddCors(options => options.AddPolicy("ExternalClients", policy =>
            {
                var origins = builder.Configuration.GetSection("ExternalAccess:AllowedOrigins").Get<string[]>() ?? [];
                if (origins.Length > 0)
                    policy.WithOrigins(origins).WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                        .WithHeaders("Authorization", "Content-Type", "MCP-Protocol-Version", "Mcp-Session-Id")
                        .WithExposedHeaders("Location", "Mcp-Session-Id");
            }));

            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/login";
                    options.LogoutPath = "/logout";
                    options.AccessDeniedPath = "/login";
                    options.ExpireTimeSpan = TimeSpan.FromDays(14);
                    options.SlidingExpiration = true;
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.Cookie.Name = "TaTaTask.Auth";
                })
                .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(ApiTokenDefaults.Scheme, _ => { });
            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy(ApiTokenDefaults.PolicyRead, policy => policy.AddAuthenticationSchemes(ApiTokenDefaults.Scheme)
                    .RequireAuthenticatedUser().RequireClaim(ApiTokenDefaults.ScopeClaim, "read"));
                options.AddPolicy(ApiTokenDefaults.PolicyWrite, policy => policy.AddAuthenticationSchemes(ApiTokenDefaults.Scheme)
                    .RequireAuthenticatedUser().RequireClaim(ApiTokenDefaults.ScopeClaim, "write"));
            });
            builder.Services.AddSignalR();

            var app = builder.Build();

            var logger = app.Services.GetRequiredService<ILogger<Program>>();

            DumpStartupDiagnostics(app, logger);

            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                logger.LogInformation("[Step 4/6] Database migration starting...");
                db.Database.Migrate();
                logger.LogInformation("[Step 4/6] Database migration OK; TaTaTask starting in {Environment} environment.", app.Environment.EnvironmentName);
            }

            if (args.Contains("--migrate-only"))
            {
                logger.LogInformation("--migrate-only mode: migration completed, exiting.");
                return;
            }

            if (ApiTokenCli.IsRequested(args))
            {
                Environment.ExitCode = await ApiTokenCli.RunAsync(app.Services, args, Console.Out, Console.Error);
                await app.DisposeAsync();
                return;
            }

            // 先恢复公网域名与 HTTPS，再执行 HSTS、认证和 MCP Origin 校验。
            if (app.Configuration.GetValue<bool>("ReverseProxy")) app.UseForwardedHeaders();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseWebAssemblyDebugging();
                logger.LogInformation("[Step 5/6] Middleware: WebAssemblyDebugging enabled (Development)");
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
                logger.LogInformation("[Step 5/6] Middleware: ExceptionHandler + HSTS enabled (Production)");
            }

            app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api")
                && !context.Request.Path.StartsWithSegments("/hubs")
                && !context.Request.Path.StartsWithSegments("/mcp") && !context.Request.Path.StartsWithSegments("/openapi")
                && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)),
                branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
            app.UseWhen(context => context.Request.Path.StartsWithSegments("/api/v1")
                || context.Request.Path.StartsWithSegments("/mcp") || context.Request.Path.StartsWithSegments("/openapi"),
                branch => branch.UseExceptionHandler(error => error.Run(context =>
                    Results.Problem(statusCode: 500, title: "服务暂时无法完成请求").ExecuteAsync(context))));

            if (app.Configuration.GetValue<bool>("ReverseProxy"))
            {
                logger.LogInformation("[Step 5/6] Middleware: ForwardedHeaders (ReverseProxy=true)");
            }
            else
            {
                logger.LogInformation("[Step 5/6] Middleware: HttpsRedirection (ReverseProxy=false)");
                app.UseHttpsRedirection();
            }

            app.UseRouting();
            app.UseCors();
            // MCP is also callable outside a browser; validate Origin explicitly, not only via CORS.
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/mcp") && context.Request.Headers.TryGetValue("Origin", out var origin))
                {
                    var allowed = app.Configuration.GetSection("ExternalAccess:AllowedOrigins").Get<string[]>() ?? [];
                    var sameOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
                    if (origin.Count != 1 || (!string.Equals(origin[0], sameOrigin, StringComparison.OrdinalIgnoreCase)
                        && !allowed.Contains(origin[0], StringComparer.OrdinalIgnoreCase)))
                    {
                        await Results.Problem(statusCode: 403, title: "Origin 不在允许列表中").ExecuteAsync(context);
                        return;
                    }
                }
                await next(context);
            });
            app.UseAuthentication();
            // CDN 只缓存静态资源；令牌响应、用户页面和实时连接不能进入共享缓存。
            app.Use(async (context, next) =>
            {
                if (context.User.Identity?.IsAuthenticated == true
                    || context.Request.Path.StartsWithSegments("/api")
                    || context.Request.Path.StartsWithSegments("/mcp")
                    || context.Request.Path.StartsWithSegments("/openapi")
                    || context.Request.Path.StartsWithSegments("/_blazor")
                    || context.Request.Path.StartsWithSegments("/hubs"))
                    context.Response.Headers.CacheControl = "no-store";
                await next(context);
            });
            app.UseAuthorization();

            app.UseAntiforgery();

            app.MapControllers();
            app.MapMcp("/mcp").RequireAuthorization(ApiTokenDefaults.PolicyRead).RequireCors("ExternalClients");
            app.MapOpenApi("/openapi/v1.json").RequireAuthorization(ApiTokenDefaults.PolicyRead).RequireCors("ExternalClients");
            app.MapHub<TodoHub>("/hubs/todo");

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode()
                .AddInteractiveWebAssemblyRenderMode()
                .AddAdditionalAssemblies(typeof(Client._Imports).Assembly);

            app.MapMethods("/Account/Logout", [HttpMethods.Get, HttpMethods.Post], async (HttpContext http) =>
            {
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.LocalRedirect("/login");
            });

            logger.LogInformation("[Step 6/6] Kestrel binding...");
            app.Run();
        }

        private static void DumpStartupDiagnostics(WebApplication app, ILogger logger)
        {
            var env = app.Environment;
            var config = app.Configuration;

            logger.LogInformation("================================================");
            logger.LogInformation("[DIAG] TaTaTask Startup Diagnostics");
            logger.LogInformation("================================================");

            logger.LogInformation("[Step 1/6] Environment:");
            logger.LogInformation("  EnvironmentName : {Env}", env.EnvironmentName);
            logger.LogInformation("  ContentRootPath : {Path}", env.ContentRootPath);
            logger.LogInformation("  WebRootPath     : {Path}", env.WebRootPath);

            logger.LogInformation("[Step 2/6] Configuration sources (load order):");
            foreach (var src in ((IConfigurationRoot)config).Providers)
            {
                logger.LogInformation("  - {Source}", src);
            }

            logger.LogInformation("[Step 3/6] Resolved config values:");
            var kestrel = config.GetSection("Kestrel:Endpoints");
            foreach (var ep in kestrel.GetChildren())
            {
                var url = ep.GetValue<string>("Url");
                logger.LogInformation("  Kestrel:Endpoints:{Name}  Url={Url}", ep.Key, url ?? "(null)");
            }
            var rp = config.GetValue<bool?>("ReverseProxy");
            logger.LogInformation("  ReverseProxy     : {Val}", rp);
            var conn = config.GetConnectionString("Default");
            logger.LogInformation("  ConnectionString : {Val}", conn);
            logger.LogInformation("================================================");
        }
    }
}
