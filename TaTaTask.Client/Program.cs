using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using TaTaTask.Client.Account;
using TaTaTask.Client.Services;

namespace TaTaTask.Client
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);

            builder.Services.AddMudServices();
            builder.Services.AddScoped(sp => new HttpClient
            {
                BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
            });

            builder.Services.AddAuthorizationCore();
            builder.Services.AddCascadingAuthenticationState();
            builder.Services.AddScoped<AuthenticationStateProvider, PersistentAuthenticationStateProvider>();

            builder.Services.AddScoped<ITodoService, ClientTodoService>();
            builder.Services.AddScoped<IScheduleService, ClientScheduleService>();

            await builder.Build().RunAsync();
        }
    }
}
