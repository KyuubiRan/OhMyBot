using System.Diagnostics.CodeAnalysis;
using OhMyTelegramBot.Interfaces.Inline;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;

namespace OhMyTelegramBot.Inline.Defs;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class SignHaFoxCoinInlineDef : IArticleInlineQuery
{
    private static readonly string[] SignTexts =
    [
        "正在撤离酸饺粥...",
        "正在逃离塔克狐...",
    ];

    public string[] QueryKeys => ["签到", "哈狐币"];
    public string Id => "sign_hafu";
    public string Title => "签个到喵";
    public string Description => "打赛博卡，赚哈狐币！";
    public InputMessageContent InputMessage => new InputTextMessageContent(SignTexts[Random.Shared.Next(SignTexts.Length)]);

    public InlineKeyboardMarkup ReplyMarkup => new()
    {
        InlineKeyboard = [[InlineKeyboardButton.WithCallbackData("签到中...")]]
    };
}