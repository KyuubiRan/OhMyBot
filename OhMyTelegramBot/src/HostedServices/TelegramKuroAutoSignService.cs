using Microsoft.Extensions.Logging;
using OhMyLib.Enums;
using OhMyLib.HostedServices;
using OhMyLib.Services;
using Telegram.Bot;

namespace OhMyTelegramBot.HostedServices;

public class TelegramKuroAutoSignService(ILogger<KuroAutoSignService> logger, BotUserService userService, ITelegramBotClient botClient)
    : KuroAutoSignService(logger, userService)
{
    protected override SoftwareType Software => SoftwareType.Telegram;

    protected override async Task SendMessage(long chatId, string message, CancellationToken cancellationToken)
    {
        await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
    }
}