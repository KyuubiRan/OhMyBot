using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OhMyLib.Enums;
using OhMyLib.HostedServices;
using Telegram.Bot;

namespace OhMyTelegramBot.HostedServices;

public class TelegramKuroAutoSignService(ILogger<TelegramKuroAutoSignService> logger, IServiceScopeFactory serviceServiceFactory, ITelegramBotClient botClient)
    : KuroAutoSignService(logger, serviceServiceFactory)
{
    protected override SoftwareType Software => SoftwareType.Telegram;

    protected override async Task SendMessage(long chatId, string message, CancellationToken cancellationToken)
    {
        await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
    }
}