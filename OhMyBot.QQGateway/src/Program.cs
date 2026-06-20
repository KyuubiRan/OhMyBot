using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OhMyBot.Contracts.Messaging;
using OhMyBot.QQGateway;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<QQGatewayOptions>(options =>
{
    options.BotInstanceId = builder.Configuration["BotInstanceId"] ?? options.BotInstanceId;
    options.CoreGrpcAddress = builder.Configuration["Core:GrpcAddress"] ?? options.CoreGrpcAddress;
    options.CommandPrefixes = builder.Configuration.GetSection("QQ:CommandPrefixes").Get<string[]>()
        ?? builder.Configuration.GetSection("CommandPrefixes").Get<string[]>()
        ?? options.CommandPrefixes;
});
builder.Services.AddOptions<RabbitMqOptions>().BindConfiguration("RabbitMQ");
builder.Services.AddSingleton<ICommandRouterClient>(_ =>
{
    var coreAddress = builder.Configuration["Core:GrpcAddress"] ?? "http://localhost:5100";
    return CommandRouterClientFactory.Create(coreAddress);
});
builder.Services.AddSingleton<QQCommandGateway>();
builder.Services.AddSingleton<QQResponseRenderer>();
builder.Services.AddHostedService<GatewayWorker>();
builder.Services.AddHostedService<RouteRefreshConsumerService>();

await builder.Build().RunAsync();
