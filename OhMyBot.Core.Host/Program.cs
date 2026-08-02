using Microsoft.AspNetCore.Server.Kestrel.Core;
using OhMyBot.Contracts;
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

// gRPC 承载命令路由与管理控制台，未配置共享令牌等于把提权入口裸奔在网络上，因此令牌始终必填、始终校验。
// 显式配置优先；没配就自动生成一份写进运行时目录并持有写锁，同机的网关自取即可，不必手抄三份。
// 跨机部署网关读不到该文件，仍须显式配置 Core:AccessToken。
GrpcSharedTokenLease? tokenLease = null;
if (string.IsNullOrWhiteSpace(builder.Configuration["Grpc:AccessToken"]))
{
    tokenLease = GrpcSharedToken.Acquire();
    builder.Configuration["Grpc:AccessToken"] = tokenLease.Token;
}

if (consoleState.Enabled)
{
    builder.Logging.ClearProviders();
}

builder.Logging.AddProvider(new InteractiveConsoleLoggerProvider(consoleOutputQueue));

builder.Services.AddGrpc(options => options.Interceptors.Add<AccessTokenInterceptor>());
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

// 写锁持有到进程退出为止；正常退出显式释放，异常退出由 OS 回收。
tokenLease?.Dispose();
