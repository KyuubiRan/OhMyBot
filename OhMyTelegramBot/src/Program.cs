// See https://aka.ms/new-console-template for more information

using System.Net;
using System.Reflection;
using FoxTail.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OhMyLib;
using OhMyLib.Attributes;
using OhMyLib.HostedServices;
using OhMyLib.Services;
using OhMyTelegramBot.Configs;
using OhMyTelegramBot.HostedServices;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot;

public static class MyBot
{
    private static readonly MyBotApplication Instance =
        new MyBotApplication.Builder()
            .ConfigureDefaultConsoleLogging()
            .ConfigureDefaultDatabase()
            .ConfigureRedisCacheIfPresent()
            .ConfigureServices((ctx, services) =>
            {
                var config = ctx.Configuration;
                services.Configure<BotConfig>(config.GetSection("Bot"));

                Assembly.GetAssembly(typeof(MyBotApplication))?.Let(services.MapComponents);
                services.MapComponents(Assembly.GetExecutingAssembly());

                services.AddHostedService<AutoConfigOwnerService>();
                services.AddHostedService<TelegramKuroAutoSignService>();
                services.AddHostedService<LogMeService>();
                services.AddHostedService<AutoCleanCacheFileService>();

                services.AddSingleton<ITelegramBotClient, TelegramBotClient>(p =>
                {
                    var cfg = p.GetRequiredService<IOptionsMonitor<BotConfig>>().CurrentValue;
                    var token = cfg.Token.IfWhiteSpaceOrNull(
                        Environment.GetEnvironmentVariable("TELEGRAME_BOT_TOKEN"));
                    if (token.IsWhiteSpaceOrNull)
                    {
                        throw new ArgumentException(
                            "Telegram bot token is not configured. Please set it in appsettings.json or environment variable 'TELEGRAME_BOT_TOKEN'");
                    }

                    HttpClient? client = null;
                    if (cfg.EnableProxy)
                    {
                        var proxyUrl = cfg.HttpProxy.Host.IfWhiteSpaceOrNull(
                            Environment.GetEnvironmentVariable("TELEGRAME_BOT_PROXY_URL"));
                        if (proxyUrl.IsWhiteSpaceOrNull)
                        {
                            throw new ArgumentException(
                                "HTTP proxy is enabled, but proxy URL is not configured. Please set it in appsettings.json or environment variable 'TELEGRAME_BOT_PROXY_URL'");
                        }

                        if (!Uri.TryCreate(proxyUrl, UriKind.Absolute, out _))
                        {
                            throw new ArgumentException(
                                "HTTP proxy URL is invalid. Please check the configuration in appsettings.json or environment variable 'TELEGRAME_BOT_PROXY_URL'");
                        }

                        var port = cfg.HttpProxy.Port;

                        client = new HttpClient(new HttpClientHandler { Proxy = new WebProxy(proxyUrl, port), UseProxy = true });

#nullable disable
                        Logger.LogInformation("Setup HTTP {ProxyUrl}:{Port} proxy for Telegram Bot Client.", proxyUrl, port);
#nullable restore
                    }

                    var botClient = new TelegramBotClient(token, client);
                    botClient.DropPendingUpdates().Wait();
                    botClient.OnUpdate += OnUpdate;

                    return botClient;
                });
            })
            .Build();

    private static readonly ILogger Logger = Instance
                                             .Services
                                             .GetRequiredService<ILoggerFactory>()
                                             .CreateLogger("Main");


    public static async Task Main()
    {
        _ = Instance.Services.GetRequiredService<ITelegramBotClient>();
        Logger.LogInformation("Bot started.");
        await Instance.StartAsync();
    }

    private static async Task OnUpdate(Update update)
    {
        _ = Task.Run(async () =>
        {
            await using var scope = Instance.Services.CreateAsyncScope();

            if (update.Message is { } m)
            {
                try
                {
                    var sp = scope.ServiceProvider;

                    var tgUserService = sp.GetRequiredService<TelegramUserService>();
                    if (m.From is { } sender)
                        await tgUserService.LogUserAsync(sender.Id, sender.Username, sender.FirstName, sender.LastName);

                    var messageHandler = sp.GetKeyedService<IMessageHandler>("handler__" + m.Type);
                    await (messageHandler?.OnReceiveMessage(m)).OrCompletedTask();
                }
                catch (Exception e)
                {
                    Logger.LogWarning(e, "Unhandled exception in processing message");
                }
            }
            else if (update.CallbackQuery is { } callback)
            {
                try
                {
                    var sp = scope.ServiceProvider;

                    var tgUserService = sp.GetRequiredService<TelegramUserService>();
                    if (callback.From is { } sender)
                        await tgUserService.LogUserAsync(sender.Id, sender.Username, sender.FirstName, sender.LastName);

                    var callbackHandler = sp.GetKeyedService<ICallbackQueryHandler>("handler__CallbackQuery");
                    await (callbackHandler?.OnReceiveCallback(callback)).OrCompletedTask();
                }
                catch (Exception e)
                {
                    Logger.LogWarning(e, "Unhandled exception in processing callback query");
                }
            }
        });
    }
}