using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OhMyLib.HostedServices;
using Telegram.Bot;

namespace OhMyTelegramBot.HostedServices;

public class TelegramAiRouterAutoSignService(
    ILogger<TelegramAiRouterAutoSignService> logger,
    IServiceScopeFactory serviceFactory,
    ITelegramBotClient botClient)
    : AiRouterAutoSignService(logger, serviceFactory)
{
    protected override async Task SendMessage(string chatId, string message, CancellationToken cancellationToken)
    {
        await botClient.SendMessage(long.Parse(chatId), message, cancellationToken: cancellationToken);
    }
}
