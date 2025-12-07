using ClientWebChat.Components;
using ClientWebChat.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Net;

int startPort = 5270;
int maxPort = 5580;
int port = startPort;
bool bound = false;

while (!bound && port <= maxPort)
{
    try
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure Kestrel to use the current port
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
            });
        });

        // Add services
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddScoped<ChatService>();

        var app = builder.Build();

        // Configure middleware
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        Console.WriteLine($"Blazor app running on port {port}");
        app.Run();

        bound = true; // Successfully started
    }
    catch (System.IO.IOException)
    {
        port++; // Try next port
        if (port > maxPort)
            throw new Exception("No available ports in range.");
    }
}
