// See https://aka.ms/new-console-template for more information

using System.Reflection;
using FoxTail.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OhMyBot;
using OhMyBot.Attributes;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot;

public static partial class MyBot
{
    public static readonly MyBotApplication Instance = new MyBotApplication.Builder()
                                                       .ConfigDefaultConsoleLogging()
                                                       .ConfigDefaultConfiguration()
                                                       .ConfigureServices(x =>
                                                       {
                                                           Assembly.GetAssembly(typeof(MyBotApplication))?.Let(x.MapComponents);
                                                           x.MapComponents(Assembly.GetExecutingAssembly());
                                                       })
                                                       .Build();

    private static readonly ILogger Logger = Instance.ServiceProvider
                                                     .GetRequiredService<ILoggerFactory>()
                                                     .CreateLogger("Main");

    private static TelegramBotClient _botClient = null!;

    public static async Task Main(string[] args)
    {
        Logger.LogInformation("OhMyTelegramBot is starting...");

        var configs = Instance.Configuration;
        var token = configs["Telegram:BotToken"] ?? Environment.GetEnvironmentVariable("TELEGRAME_BOT_TOKEN");
        
        if (string.IsNullOrWhiteSpace(token))
        {
            Logger.LogError("Telegram bot token is not configured. Please set the 'Telegram:BotToken' configuration.");
            Environment.Exit(1);
        }

        var botClient = _botClient = new TelegramBotClient(token);
        botClient.OnUpdate += OnUpdate;

        Logger.LogInformation("OhMyTelegramBot has started.");

        await Task.Delay(-1);
    }

    private static async Task OnUpdate(Update update)
    {
        if (update.Message is { } m)
        {
            LogReceivedMessage(Logger, m.Chat.Id, m.From?.Id ?? 0, m.Text ?? "[non-text message]");
            await _botClient.SendMessage(update.Message.Chat.Id, m.Text ?? "[non-text message]");
        }
    }

    [LoggerMessage(LogLevel.Information, "Received message from CID={chatId} (SID={senderId}): {text}")]
    static partial void LogReceivedMessage(ILogger logger, long chatId, long senderId, string text);
}