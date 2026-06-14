using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OhMyLib.HostedServices;
using OhMyLib.Services.AiRouter;
using Telegram.Bot;
using Telegram.Bot.Extensions;
using Telegram.Bot.Types.Enums;
using OhMyTelegramBot.Extensions;

namespace OhMyTelegramBot.HostedServices;

public class TelegramAiRouterAutoSignService(
    ILogger<TelegramAiRouterAutoSignService> logger,
    IServiceScopeFactory serviceFactory,
    ITelegramBotClient botClient)
    : AiRouterAutoSignService(logger, serviceFactory)
{
    protected override string BuildSignMessage(AiRouterSignResult result, DateTimeOffset time)
    {
        return string.Join('\n',
            Markdown.Escape("[AI Router-自动签到]"),
            result.ToMarkdownV2Message(),
            $"时间：{Markdown.Escape(time.ToString("yyyy-MM-dd HH:mm:ss"))}");
    }

    protected override string BuildFailureMessage(Exception exception)
    {
        return string.Join('\n',
            Markdown.Escape("[AI Router-自动签到]"),
            $"自动签到执行失败：{Markdown.Escape(exception.GetBaseException().Message)}");
    }

    protected override async Task SendMessage(string chatId, string message, CancellationToken cancellationToken)
    {
        await botClient.SendMessage(long.Parse(chatId), message, ParseMode.MarkdownV2, cancellationToken: cancellationToken);
    }
}
