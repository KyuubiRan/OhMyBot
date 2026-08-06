using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using OhMyBot.Contracts;
using OhMyBot.Contracts.Messaging;
using OhMyBot.Logging;
using OhMyBot.TelegramGateway;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddOhMyBotFileLogging(builder.Configuration);

// 同机部署时 Core 启动会把令牌写进运行时目录，这里没显式配就自动取，省掉手抄。
var coreAccessToken = GrpcSharedToken.Resolve(builder.Configuration["Core:AccessToken"]);
if (string.IsNullOrWhiteSpace(coreAccessToken))
{
    throw new InvalidOperationException(
        $"Core:AccessToken 未配置，且未能从 {GrpcSharedToken.ResolvePath()} 读到 Core 自动生成的令牌。" +
        "同机部署请先启动 Core；跨机部署请在 appsettings.json 里显式配置。");
}

builder.Services.Configure<TelegramGatewayOptions>(options =>
{
    options.BotInstanceId = builder.Configuration["BotInstanceId"] ?? options.BotInstanceId;
    options.BotToken = builder.Configuration["Telegram:BotToken"] ?? options.BotToken;
    options.HttpProxy = builder.Configuration["Telegram:HttpProxy"] ?? options.HttpProxy;
    options.CoreGrpcAddress = builder.Configuration["Core:GrpcAddress"] ?? options.CoreGrpcAddress;
    options.CoreAccessToken = coreAccessToken;
    options.DropPendingUpdates = builder.Configuration.GetValue("Telegram:DropPendingUpdates", options.DropPendingUpdates);
    options.CommandPrefixes = builder.Configuration.GetSection("Telegram:CommandPrefixes").Get<string[]>()
        ?? builder.Configuration.GetSection("CommandPrefixes").Get<string[]>()
        ?? options.CommandPrefixes;
});
builder.Services.AddOptions<RabbitMqOptions>().BindConfiguration("RabbitMQ");

builder.Services.AddSingleton<ICommandRouterClient>(_ =>
{
    var coreAddress = builder.Configuration["Core:GrpcAddress"] ?? "http://localhost:5100";
    return CommandRouterClientFactory.Create(coreAddress, coreAccessToken);
});
builder.Services.AddSingleton<ITelegramBotClient>(_ =>
{
    var token = builder.Configuration["Telegram:BotToken"];
    if (string.IsNullOrWhiteSpace(token))
    {
        throw new InvalidOperationException("Telegram:BotToken is required.");
    }

    var proxy = builder.Configuration["Telegram:HttpProxy"];
    if (string.IsNullOrWhiteSpace(proxy))
    {
        return new TelegramBotClient(token);
    }

    var httpClient = new HttpClient(new SocketsHttpHandler
    {
        Proxy = new WebProxy(proxy),
        UseProxy = true
    });
    return new TelegramBotClient(token, httpClient);
});
builder.Services.AddSingleton<TelegramCommandGateway>();
builder.Services.AddSingleton<TelegramResponseRenderer>();
builder.Services.AddSingleton<TelegramUpdateHandler>();
builder.Services.AddHostedService<GatewayWorker>();
builder.Services.AddHostedService<RouteRefreshConsumerService>();
builder.Services.AddHostedService<TelegramNotificationConsumerService>();

await builder.Build().RunAsync();
