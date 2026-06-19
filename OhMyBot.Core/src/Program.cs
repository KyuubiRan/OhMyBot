using OhMyBot.Core;
using OhMyBot.Core.Grpc;
using OhMyBot.Core.Terminal;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
var consoleState = new InteractiveConsoleState();
var consoleOutputQueue = new InteractiveConsoleOutputQueue();
consoleState.AttachOutputQueue(consoleOutputQueue);

if (consoleState.Enabled)
{
    builder.Logging.ClearProviders();
    builder.Logging.AddProvider(new InteractiveConsoleLoggerProvider(consoleState));
}

builder.Services.AddGrpc();
builder.Services.AddSingleton(consoleState);
builder.Services.AddSingleton(consoleOutputQueue);
builder.Services.AddOhMyBotCoreDatabase(builder.Configuration);
builder.Services.AddOhMyBotCoreRedis(builder.Configuration);
builder.Services.AddOhMyBotCoreServices();

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
