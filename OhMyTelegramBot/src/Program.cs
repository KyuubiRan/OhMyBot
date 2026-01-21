// See https://aka.ms/new-console-template for more information

using System.Net;
using System.Reflection;
using FoxTail.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OhMyLib;
using OhMyLib.Attributes;
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
            .ConfigureDefaultConfiguration()
            .ConfigureRedisCacheIfPresent()
            .ConfigureServices((services, configManager) =>
            {
                services.Configure<BotConfig>(configManager.GetSection("Bot"));

                Assembly.GetAssembly(typeof(MyBotApplication))?.Let(services.MapComponents);
                services.MapComponents(Assembly.GetExecutingAssembly());

                services.AddHostedService<AutoConfigOwnerService>();

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
                    botClient.OnUpdate += OnUpdate;

                    return botClient;
                });
            })
            .Build();

    private static readonly ILogger Logger = Instance
                                             .ServiceProvider
                                             .GetRequiredService<ILoggerFactory>()
                                             .CreateLogger("Main");

    private static readonly CancellationTokenSource Cts = new();

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        Logger.LogInformation("Shutdown requested, exiting...");
        Cts.Cancel();
        e.Cancel = true;
    }

    public static async Task Main()
    {
        _ = Instance.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        Logger.LogInformation("Bot started.");

        // await botClient.TestApi();
        // Logger.LogInformation("Telegram Bot API is working fine.");

        Console.CancelKeyPress += OnCancelKeyPress;

        await Instance.StartAsync(Cts.Token);
    }


    private static async Task OnUpdate(Update update)
    {
        await using var scope = Instance.ServiceProvider.CreateAsyncScope();

        if (update.Message is { } m)
        {
            try
            {
                scope.ServiceProvider.GetKeyedService<IMessageHandler>("handler__" + m.Type)?.Let(async handler => { await handler.OnReceiveMessage(m); });
            }
            catch (Exception e)
            {
                Logger.LogWarning(e, "Unhandled exception in processing message");
            }
        }
    }
}