// See https://aka.ms/new-console-template for more information

using System.Net;
using System.Reflection;
using FoxTail.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OhMyBot;
using OhMyBot.Attributes;
using OhMyTelegramBot.MessageHandlers;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace OhMyTelegramBot;

public static class MyBot
{
#pragma warning disable CA1873
    private static readonly MyBotApplication Instance = new MyBotApplication.Builder()
        .ConfigDefaultConsoleLogging()
        .ConfigDefaultConfiguration()
        .ConfigureServices((x, c) =>
        {
            Assembly.GetAssembly(typeof(MyBotApplication))?.Let(x.MapComponents);
            x.MapComponents(Assembly.GetExecutingAssembly());
            x.AddSingleton<IConfigurationManager, ConfigurationManager>();
            x.AddSingleton<ITelegramBotClient, TelegramBotClient>(_ =>
            {
                var token = c["Bot:Token"] ?? Environment.GetEnvironmentVariable("TELEGRAME_BOT_TOKEN");
                if (token.IsWhiteSpaceOrNull)
                {
                    throw new ArgumentException(
                        "Telegram bot token is not configured. Please set it in appsettings.json or environment variable 'TELEGRAME_BOT_TOKEN'");
                }

                var enableProxy = c["Bot:EnableProxy"]?.ToLower() == "true";
                HttpClient? client = null;
                if (enableProxy)
                {
                    var proxyUrl = c["Bot:HttpProxy:Host"] ?? Environment.GetEnvironmentVariable("TELEGRAME_BOT_PROXY_URL");
                    if (proxyUrl.IsWhiteSpaceOrNull)
                    {
                        throw new ArgumentException(
                            "HTTP proxy is enabled, but proxy URL is not configured. Please set it in appsettings.json or environment variable 'TELEGRAME_BOT_PROXY_URL'");
                    }

                    if (!Uri.TryCreate(proxyUrl, UriKind.Absolute, out var _))
                    {
                        throw new ArgumentException(
                            "HTTP proxy URL is invalid. Please check the configuration in appsettings.json or environment variable 'TELEGRAME_BOT_PROXY_URL'");
                    }

                    var port = c["Bot:HttpProxy:Port"]?.Let(port => int.TryParse(port, out var p) ? p : 7890) ?? 7890;

                    client = new HttpClient(new HttpClientHandler { Proxy = new WebProxy(proxyUrl, port), UseProxy = true });
                    Logger?.LogInformation("Setup HTTP {ProxyUrl}:{Port} proxy for Telegram Bot Client.", proxyUrl, port);
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

    public static async Task Main(string[] args)
    {
        var botClient = Instance.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        Logger.LogInformation("Bot started.");

        await botClient.TestApi();
        Logger.LogInformation("Telegram Bot API is working fine.");

        Console.CancelKeyPress += OnCancelKeyPress;

        try
        {
            await Task.Delay(-1, Cts.Token);
        }
        catch (TaskCanceledException)
        {
        }
    }

#pragma warning restore CA1873

    private static async Task OnUpdate(Update update)
    {
        await using var scope = Instance.ServiceProvider.CreateAsyncScope();
        if (update.Message is { } m)
        {
            try
            {
                switch (m.Type)
                {
                    case MessageType.Text:
                    {
                        await scope.ServiceProvider.GetRequiredService<PlantTextHandler>().OnReceiveTextMessage(m);
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning("Unhandled exception in processing message: {Exception}", e);
            }
        }
    }
}