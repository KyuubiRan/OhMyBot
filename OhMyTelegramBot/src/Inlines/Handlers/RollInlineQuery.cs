using FoxTail.Common.Utils;
using FoxTail.Extensions;
using OhMyLib.Attributes;
using OhMyLib.Utils;
using OhMyTelegramBot.Interfaces.Handlers;
using Telegram.Bot;
using Telegram.Bot.Extensions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace OhMyTelegramBot.Inlines.Handlers;

[Component("inline_chosen_query_handler__roll")]
public class RollInlineQuery(ITelegramBotClient botClient) : IInlineChosenQueryHandler
{
    public async Task OnReceiveChosenInlineQuery(ChosenInlineResult chosenInlineResult)
    {
        if (chosenInlineResult.InlineMessageId == null)
            return;

        try
        {
            var query = chosenInlineResult.Query.IfWhiteSpaceOrNull("d100");
            var result = DiceRoller.Roll(query);

            var str = StringUtils.BuildString(sb =>
            {
                sb.Append('`')
                  .Append(Markdown.Escape(result.Expression))
                  .Append("` \\= `")
                  .Append(Markdown.Escape(result.Breakdown))
                  .Append("` \\= ")
                  .Append($"*{result.Total}*");
            });

            await botClient.EditMessageText(chosenInlineResult.InlineMessageId, str, ParseMode.MarkdownV2);
        }
        catch (Exception e)
        {
            await botClient.EditMessageText(chosenInlineResult.InlineMessageId, e.Message);
        }
    }
}