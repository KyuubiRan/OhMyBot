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
using OhMyTelegramBot.Extensions;
using OhMyTelegramBot.HostedServices;
using OhMyTelegramBot.Interfaces;
using OhMyTelegramBot.Interfaces.Handlers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot;

public class Application
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
                    var logger = p.GetRequiredService<ILogger<Application>>();

                    var cfg = p.GetRequiredService<IOptionsMonitor<BotConfig>>().CurrentValue;
                    var token = cfg.Token.IfWhiteSpaceOrNull(Environment.GetEnvironmentVariable("TELEGRAME_BOT_TOKEN"));
                    if (token.IsWhiteSpaceOrNull)
                    {
                        throw new ArgumentException(
                            "Telegram bot token is not configured. Please set it in appsettings.json or environment variable 'TELEGRAME_BOT_TOKEN'");
                    }

                    HttpClient? client = null;
                    if (cfg.EnableProxy)
                    {
                        var proxyUrl = cfg.HttpProxy.Host.IfWhiteSpaceOrNull(Environment.GetEnvironmentVariable("TELEGRAME_BOT_PROXY_URL"));

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

                        logger.LogInformation("Setup HTTP {ProxyUrl}:{Port} proxy for Telegram Bot Client.", proxyUrl, port);
                    }

                    var botClient = new TelegramBotClient(token, client);
                    botClient.DropPendingUpdates().Wait();
                    botClient.OnUpdate += OnUpdate;

                    return botClient;
                });
            })
            .Build();

    private static readonly ILogger Logger = Instance.Services.GetRequiredService<ILogger<Application>>();

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
            var sp = scope.ServiceProvider;
            var tUserService = sp.GetRequiredService<TelegramUserService>();

            if (update.Message is { } m)
            {
                try
                {
                    if (m.From is { } sender)
                        await tUserService.LogUserAsync(sender);

                    await (sp.GetKeyedService<IMessageHandler>("handler__" + m.Type)?.OnReceiveMessage(m)).OrCompletedTask();
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
                    await tUserService.LogUserAsync(callback.From);

                    await (sp.GetKeyedService<ICallbackQueryHandler>("handler__CallbackQuery")?.OnReceiveCallbackQuery(callback)).OrCompletedTask();
                }
                catch (Exception e)
                {
                    Logger.LogWarning(e, "Unhandled exception in processing callback query");
                }
            }
            else if (update.InlineQuery is { } inlineQuery)
            {
                try
                {
                    await tUserService.LogUserAsync(inlineQuery.From);

                    await (sp.GetKeyedService<IInlineQueryHandler>("handler__InlineQuery")?.OnReceiveInlineQuery(inlineQuery)).OrCompletedTask();
                }
                catch (Exception e)
                {
                    Logger.LogWarning(e, "Unhandled exception in processing inline query");
                }
            }
        });
    }
}