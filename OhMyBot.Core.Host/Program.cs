using Microsoft.AspNetCore.Server.Kestrel.Core;
using OhMyBot.Core;
using OhMyBot.Core.Host;
using OhMyBot.Core.Infrastructure.Grpc;
using OhMyBot.Core.Infrastructure.Terminal;
using OhMyBot.Core.Host.Plugins;

var startupArguments = HostStartupArguments.Parse(args);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = startupArguments.HostArguments,
    ContentRootPath = AppContext.BaseDirectory
});

if (startupArguments.RemoteConsoleRequested)
{
    if (!await RemoteConsoleClient.TryRunAsync(builder.Configuration))
    {
        Console.Error.WriteLine(
            $"无法连接 Core 远程控制台：{builder.Configuration["Core:GrpcAddress"] ?? "http://localhost:5100"}");
        Environment.ExitCode = 1;
    }

    return;
}

var consoleState = new InteractiveConsoleState();
var consoleOutputQueue = new InteractiveConsoleOutputQueue();
consoleState.AttachOutputQueue(consoleOutputQueue);

if (consoleState.Enabled)
{
    builder.Logging.ClearProviders();
}

builder.Logging.AddProvider(new InteractiveConsoleLoggerProvider(consoleOutputQueue));

builder.Services.AddGrpc();
builder.Services.AddSingleton(consoleState);
builder.Services.AddSingleton(consoleOutputQueue);
builder.Services.AddOhMyBotCoreDatabase(builder.Configuration);
builder.Services.AddOhMyBotCoreRedis(builder.Configuration);
builder.Services.AddOhMyBotCoreServices();
builder.Services.AddOhMyBotPluginRuntime(builder.Configuration);

if (!builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenLocalhost(5100, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
    });
}

var app = builder.Build();

app.MapGrpcService<CommandRouterGrpcService>();
app.MapGet("/", () => "OhMyBot Core v2 gRPC service is running.");

app.Run();
