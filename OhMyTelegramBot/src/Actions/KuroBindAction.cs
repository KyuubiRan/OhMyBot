using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Services.Kuro;
using OhMyTelegramBot.Interfaces.Handlers;
using OhMyTelegramBot.Models;
using OhMyTelegramBot.Models.ActionData;
using OhMyTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Actions;

[Component(Key = "action__kuro_bind")]
public sealed class KuroBindAction(BotActionManager actionManager, KuroAccountService kuroAccountService) : IBotCallbackActionHandler
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
            await kuroAccountService.BindAsync(
                query.From.Id,
                SoftwareType.Telegram,
                new KuroBindPayload(data.KuroUserId, data.KuroToken ?? string.Empty, data.KuroDevCode, data.KuroDistinctId, data.IpAddress));

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