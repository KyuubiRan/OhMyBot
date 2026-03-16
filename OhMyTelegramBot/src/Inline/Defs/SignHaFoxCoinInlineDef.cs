using System.Diagnostics.CodeAnalysis;
using OhMyTelegramBot.Interfaces.Inline;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;

namespace OhMyTelegramBot.Inline.Defs;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class SignHaFoxCoinInlineDef : IArticleInlineQuery
{
    public string[] QueryKeys => ["签到", "哈狐币"];
    public string Id => "sign_hafu";
    public string Title => "喵喵喵";
    public InputMessageContent InputMessage => new InputTextMessageContent("签到中...");
    public InlineKeyboardMarkup ReplyMarkup => InlineKeyboardMarkup.Empty();
}