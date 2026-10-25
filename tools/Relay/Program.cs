using System.Net.WebSockets;
using LastEdict.Relay;

// Посредник сетевой игры Last Edict (docs/network.md §6). Адрес — ASPNETCORE_URLS (по умолчанию http://0.0.0.0:8790), путь под обратным
// прокси — RELAY_PATH_BASE (например /le-relay), каталог асинхронных комнат — RELAY_DATA. Секретов нет: доступ к комнате — по её коду.
if (args.Contains("--health"))   // проверка контейнера: в образе нет curl
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try
    {
        return (await http.GetAsync(new Uri("http://127.0.0.1:8790/health")).ConfigureAwait(false)).IsSuccessStatusCode ? 0 : 1;
    }
    catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
    {
        return 1;
    }
}
var app = RelayServer.Create(args, RelayOptions.FromEnvironment());
await app.RunAsync().ConfigureAwait(false);
return 0;

namespace LastEdict.Relay
{
    public static class RelayServer
    {
        /// <summary>Приложение посредника: <c>/ws</c> — WebSocket, <c>/health</c> — строка состояния.</summary>
        public static WebApplication Create(string[] args, RelayOptions options, Action<WebApplicationBuilder>? configure = null)
        {
            var builder = WebApplication.CreateSlimBuilder(args);
            if (string.IsNullOrEmpty(builder.Configuration["urls"]) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
                builder.WebHost.UseUrls("http://0.0.0.0:8790");
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton<RelayHub>();
            configure?.Invoke(builder);
            var app = builder.Build();
            if (Environment.GetEnvironmentVariable("RELAY_PATH_BASE") is { Length: > 0 } pathBase) app.UsePathBase(pathBase);
            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
            var hub = app.Services.GetRequiredService<RelayHub>();
            bool trustProxy = Environment.GetEnvironmentVariable("RELAY_TRUST_PROXY") == "1";
            app.MapGet("/health", () => $"ok rooms={hub.Rooms} connections={hub.Connections}");
            app.Map("/ws", async context =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
                // За обратным прокси (RELAY_TRUST_PROXY=1) адрес — из X-Real-IP: по нему считаются попытки входа.
                string address = (trustProxy ? context.Request.Headers["X-Real-IP"].FirstOrDefault() : null)
                                 ?? context.Connection.RemoteIpAddress?.ToString() ?? "";
                // Остановка сервера (перезапуск контейнера) закрывает соединения сразу — клиенты переподключатся.
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, app.Lifetime.ApplicationStopping);
                await hub.Serve(socket, address, stop.Token).ConfigureAwait(false);
            });
            var sweep = new Timer(_ => hub.Sweep(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            app.Lifetime.ApplicationStopping.Register(() => sweep.Dispose());
            return app;
        }
    }
}
