// See https://aka.ms/new-console-template for more information

using System.Reflection;
using FoxTail.Extensions;
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
    private static readonly MyBotApplication Instance =
        new MyBotApplication.Builder()
            .ConfigDefaultConsoleLogging()
            .ConfigDefaultConfiguration()
            .ConfigureServices((x, c) =>
            {
                Assembly.GetAssembly(typeof(MyBotApplication))?.Let(x.MapComponents);
                x.MapComponents(Assembly.GetExecutingAssembly());
                x.AddSingleton<ITelegramBotClient, TelegramBotClient>(_ =>
                {
                    var token = c["Bot:Token"] ?? Environment.GetEnvironmentVariable("TELEGRAME_BOT_TOKEN");
                    if (token.IsWhiteSpaceOrNull)
                    {
                        Console.Error.WriteLine(
                            "Telegram bot token is not configured. Please set it in appsettings.json or environment variable 'TELEGRAME_BOT_TOKEN'");
                        Environment.Exit(1);
                    }

                    var botClient = new TelegramBotClient(token);
                    botClient.OnUpdate += OnUpdate;

                    return botClient;
                });
            })
            .Build();

    private static readonly ILogger Logger = Instance
                                             .ServiceProvider
                                             .GetRequiredService<ILoggerFactory>()
                                             .CreateLogger("Main");

    public static async Task Main(string[] args)
    {
        _ = Instance.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        Logger.LogInformation("Bot started.");
        
        await Task.Delay(-1);
    }

    private static async Task OnUpdate(Update update)
    {
        await using var scope = Instance.ServiceProvider.CreateAsyncScope();
        if (update.Message is { } m)
        {
            switch (m.Type)
            {
                case MessageType.Text:
                {
                    scope.ServiceProvider.GetRequiredService<PlantTextHandler>().OnReceiveTextMessage(m);
                    break;
                }
            }
        }
    }
}