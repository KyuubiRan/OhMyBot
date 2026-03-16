using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using OhMyLib.Attributes;
using OhMyTelegramBot.Interfaces.Handlers;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Inline;

[Component("inline_chosen_query_handler__sign_hafu")]
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class SignHaFoxCoinInlineQuery(ITelegramBotClient botClient, ILogger<SignHaFoxCoinInlineQuery> logger) : IInlineChosenQueryHandler
{
    public async Task OnReceiveChosenInlineQuery(ChosenInlineResult chosenInlineResult)
    {
        if (chosenInlineResult.InlineMessageId == null)
            return;

        await Task.Delay(1000);
        await botClient.EditMessageText(inlineMessageId: chosenInlineResult.InlineMessageId, "签到成功，获得999哈狐币");
    }
}