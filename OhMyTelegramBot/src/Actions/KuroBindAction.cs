using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Services;
using OhMyTelegramBot.Interfaces;
using OhMyTelegramBot.Models;
using OhMyTelegramBot.Models.ActionData;
using OhMyTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Actions;

[Component(Key = "action__kuro_bind")]
public class KuroBindAction(BotActionManager actionManager, KuroUserService kuroUserService) : IBotActionHandler
{
    public async Task OnReceiveAction(ITelegramBotClient botClient, CallbackQuery query, BotAction action)
    {
        var data = await actionManager.GetActionDataAsync<KuroBindActionData>(action.Hash);
        if (data == null)
            return;

        var message = query.Message;

        if (message == null)
            return;

        if (data.Confirm)
        {
            await kuroUserService.CreateOrUpdateUserAsync(
                query.From.Id,
                SoftwareType.Telegram,
                data.KuroUserId,
                data.KuroToken,
                data.KuroDevCode,
                data.KuroDistinctId,
                data.IpAddress
            );

            await botClient.EditMessageText(
                message.Chat.Id,
                message.Id,
                "绑定成功！"
            );
        }
        else
        {
            await botClient.EditMessageText(
                message.Chat.Id,
                message.Id,
                "绑定已取消"
            );
        }
    }
}