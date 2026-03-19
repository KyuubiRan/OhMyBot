using System.Diagnostics.CodeAnalysis;
using OhMyTelegramBot.Interfaces.Inline;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;

namespace OhMyTelegramBot.Inlines.Defs;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class RollInlineDef : IArticleInlineQuery
{
    public int Priority => int.MaxValue - 1;

    public string[] QueryKeys => ["roll", "r"];
    public string Id => "roll";
    public string Title => "Roll";
    public string Description => "Roll个点数";
    public InputMessageContent InputMessage => new InputTextMessageContent("运气如何呢...");

    public InlineKeyboardMarkup ReplyMarkup => new()
    {
        InlineKeyboard = [[InlineKeyboardButton.WithCallbackData("Roll点中...")]]
    };
}