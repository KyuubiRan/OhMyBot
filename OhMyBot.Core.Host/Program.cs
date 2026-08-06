using Microsoft.AspNetCore.Server.Kestrel.Core;
using OhMyBot.Contracts;
using OhMyBot.Core;
using OhMyBot.Core.Host;
using OhMyBot.Core.Infrastructure.Grpc;
using OhMyBot.Core.Infrastructure.Terminal;
using OhMyBot.Core.Host.Plugins;
using OhMyBot.Logging;

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

// 必须在上面的 ClearProviders 之后：交互式控制台会清掉所有 provider。
builder.Logging.AddOhMyBotFileLogging(builder.Configuration);

// 以 systemd 服务运行时改用 Type=notify 协议：ApplicationStarted（即 Kestrel 绑定 gRPC 端口之后）
// 才上报 READY=1，两个网关的 unit 靠 After= 就能真正等到 Core 可连，而不是刚 fork 出来就被放行。
// 非 systemd 环境（本地直接跑）下这个调用不生效，不影响交互式控制台。
builder.Host.UseSystemd();

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
