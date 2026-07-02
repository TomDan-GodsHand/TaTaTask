using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using System.Net;
using TaTaTask.Client.Services;
using TaTaTask.Components;
using TaTaTask.Components.Account;
using TaTaTask.Data;
using TaTaTask.Hubs;
using TaTaTask.Services;

namespace TaTaTask
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Integrate with systemd (Type=notify lifetime + journal-formatted console logging
            // when running under systemd; no-op otherwise, e.g. on Windows/dev).
            builder.Host.UseSystemd();

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents()
                .AddInteractiveWebAssemblyComponents();

            builder.Services.AddMudServices();
            builder.Services.AddControllers();
            builder.Services.AddCascadingAuthenticationState();

            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

            builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();

            builder.Services.AddScoped<AuthenticationStateProvider, PersistingServerAuthenticationStateProvider>();

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentUser, CurrentUser>();
            builder.Services.AddScoped<ITodoService, ServerTodoService>();

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
                });
            builder.Services.AddAuthorization();
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

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

            if (app.Configuration.GetValue<bool>("ReverseProxy"))
            {
                logger.LogInformation("[Step 5/6] Middleware: ForwardedHeaders (ReverseProxy=true)");
                app.UseForwardedHeaders(new ForwardedHeadersOptions
                {
                    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
#pragma warning disable ASPDEPR005
                    KnownNetworks = { new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Loopback, 8) }
#pragma warning restore ASPDEPR005
                });
            }
            else
            {
                logger.LogInformation("[Step 5/6] Middleware: HttpsRedirection (ReverseProxy=false)");
                app.UseHttpsRedirection();
            }

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseAntiforgery();

            app.MapControllers();
            app.MapHub<TodoHub>("/hubs/todo");

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode()
                .AddInteractiveWebAssemblyRenderMode()
                .AddAdditionalAssemblies(typeof(Client._Imports).Assembly);

            app.MapPost("/Account/Logout", async (HttpContext http) =>
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
