using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OhMyLib.Enums;
using OhMyLib.HostedServices;
using Telegram.Bot;

namespace OhMyTelegramBot.HostedServices;

public class TelegramKuroAutoSignService(ILogger<TelegramKuroAutoSignService> logger, IServiceScopeFactory serviceProvider, ITelegramBotClient botClient)
    : KuroAutoSignService(logger, serviceProvider)
{
    protected override SoftwareType Software => SoftwareType.Telegram;

    protected override async Task SendMessage(long chatId, string message, CancellationToken cancellationToken)
    {
        await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
    }
}