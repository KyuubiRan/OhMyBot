using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OhMyBot.Contracts;
using OhMyBot.Contracts.Messaging;
using OhMyBot.OneBotV11;
using OhMyBot.OneBotV11.Transport;
using OhMyBot.QQGateway;

var builder = Host.CreateApplicationBuilder(args);

// 同机部署时 Core 启动会把令牌写进运行时目录，这里没显式配就自动取，省掉手抄。
var coreAccessToken = GrpcSharedToken.Resolve(builder.Configuration["Core:AccessToken"]);
if (string.IsNullOrWhiteSpace(coreAccessToken))
{
    throw new InvalidOperationException(
        $"Core:AccessToken 未配置，且未能从 {GrpcSharedToken.ResolvePath()} 读到 Core 自动生成的令牌。" +
        "同机部署请先启动 Core；跨机部署请在 appsettings.json 里显式配置。");
}

builder.Services.Configure<QQGatewayOptions>(options =>
{
    options.BotInstanceId = builder.Configuration["BotInstanceId"] ?? options.BotInstanceId;
    options.CoreGrpcAddress = builder.Configuration["Core:GrpcAddress"] ?? options.CoreGrpcAddress;
    options.CoreAccessToken = coreAccessToken;
    options.CommandPrefixes = builder.Configuration.GetSection("QQ:CommandPrefixes").Get<string[]>()
        ?? builder.Configuration.GetSection("CommandPrefixes").Get<string[]>()
        ?? options.CommandPrefixes;
});
builder.Services.AddOptions<RabbitMqOptions>().BindConfiguration("RabbitMQ");
builder.Services.AddSingleton<ICommandRouterClient>(_ =>
{
    var coreAddress = builder.Configuration["Core:GrpcAddress"] ?? "http://localhost:5100";
    return CommandRouterClientFactory.Create(coreAddress, coreAccessToken);
});
builder.Services.AddSingleton<IOneBotClient>(_ =>
{
    var endpoint = builder.Configuration["OneBot:Endpoint"] ?? "ws://localhost:3001";
    var accessToken = builder.Configuration["OneBot:AccessToken"];
    var uri = new Uri(endpoint);
    return uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase)
        ? OneBotClient.CreateHttpClient(new OneBotHttpOptions { BaseUri = uri, AccessToken = accessToken })
        : OneBotClient.CreateWebsocketClient(new OneBotWebSocketOptions { Uri = uri, AccessToken = accessToken });
});
builder.Services.AddSingleton<QQCommandGateway>();
builder.Services.AddSingleton<QQResponseRenderer>();
builder.Services.AddSingleton<QQUpdateHandler>();
builder.Services.AddHostedService<GatewayWorker>();
builder.Services.AddHostedService<RouteRefreshConsumerService>();
builder.Services.AddHostedService<QQNotificationConsumerService>();

await builder.Build().RunAsync();
