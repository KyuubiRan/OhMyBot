using Microsoft.Extensions.Logging;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Services;
using OhMyTelegramBot.Interfaces.Handlers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Inlines.Handlers;

[Component("inline_chosen_query_handler__sign_hafu")]
public class SignHaFoxCoinInlineQuery(
    ITelegramBotClient botClient,
    BotUserCheckinService checkinService,
    ILogger<SignHaFoxCoinInlineQuery> logger
) : IInlineChosenQueryHandler
{
    public async Task OnReceiveChosenInlineQuery(ChosenInlineResult chosenInlineResult)
    {
        if (chosenInlineResult.InlineMessageId == null)
            return;

        var result = await checkinService.CheckinAsync(chosenInlineResult.From.Id.ToString(), SoftwareType.Telegram);
        await botClient.EditMessageText(chosenInlineResult.InlineMessageId, result.ToString());
    }
}