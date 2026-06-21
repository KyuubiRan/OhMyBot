using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OhMyBot.Contracts.Messaging;
using OhMyBot.TelegramGateway;
using OhMyBot.TelegramGateway.Rendering;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<TelegramGatewayOptions>(options =>
{
    options.BotInstanceId = builder.Configuration["BotInstanceId"] ?? options.BotInstanceId;
    options.BotToken = builder.Configuration["Telegram:BotToken"] ?? options.BotToken;
    options.CoreGrpcAddress = builder.Configuration["Core:GrpcAddress"] ?? options.CoreGrpcAddress;
    options.DropPendingUpdates = builder.Configuration.GetValue("Telegram:DropPendingUpdates", options.DropPendingUpdates);
    options.CommandPrefixes = builder.Configuration.GetSection("Telegram:CommandPrefixes").Get<string[]>()
        ?? builder.Configuration.GetSection("CommandPrefixes").Get<string[]>()
        ?? options.CommandPrefixes;
});
builder.Services.AddOptions<RabbitMqOptions>().BindConfiguration("RabbitMQ");

builder.Services.AddSingleton<ICommandRouterClient>(_ =>
{
    var coreAddress = builder.Configuration["Core:GrpcAddress"] ?? "http://localhost:5100";
    return CommandRouterClientFactory.Create(coreAddress);
});
builder.Services.AddSingleton<ITelegramBotClient>(_ =>
{
    var token = builder.Configuration["Telegram:BotToken"];
    if (string.IsNullOrWhiteSpace(token))
    {
        throw new InvalidOperationException("Telegram:BotToken is required.");
    }

    return new TelegramBotClient(token);
});
builder.Services.AddSingleton<TelegramCommandGateway>();
builder.Services.AddSingleton<ITelegramCommandResultRenderer, PingTelegramRenderer>();
builder.Services.AddSingleton<ITelegramCommandResultRenderer, UserInfoTelegramRenderer>();
builder.Services.AddSingleton<ITelegramCommandResultRenderer, LinkTelegramRenderer>();
builder.Services.AddSingleton<ITelegramCommandResultRenderer, AiRouterTelegramRenderer>();
builder.Services.AddSingleton<ITelegramCommandResultRenderer, KuroTelegramRenderer>();
builder.Services.AddSingleton<ITelegramCommandResultRenderer, NotifyTelegramRenderer>();
builder.Services.AddSingleton<ITelegramCommandResultRenderer, HelpTelegramRenderer>();
builder.Services.AddSingleton<ITelegramCommandResultRenderer, FallbackTelegramRenderer>();
builder.Services.AddSingleton<TelegramResponseRenderer>();
builder.Services.AddSingleton<TelegramUpdateHandler>();
builder.Services.AddHostedService<GatewayWorker>();
builder.Services.AddHostedService<RouteRefreshConsumerService>();
builder.Services.AddHostedService<TelegramNotificationConsumerService>();

await builder.Build().RunAsync();
