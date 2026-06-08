using OhMyLib.Attributes;
using OhMyLib.Services.AiRouter;
using OhMyTelegramBot.Extensions;
using OhMyTelegramBot.Interfaces.Handlers;
using OhMyTelegramBot.Models;
using OhMyTelegramBot.Models.ActionData;
using OhMyTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace OhMyTelegramBot.Actions;

[Component(Key = "action__ai_router_delete_select")]
public sealed class AiRouterDeleteSelectAction(BotActionManager actionManager, AiRouterAccountService accountService) : IBotCallbackActionHandler
{
    public async Task OnReceiveAction(ITelegramBotClient botClient, CallbackQuery query, BotAction action)
    {
        var data = await actionManager.GetActionDataAsync<AiRouterDeleteSelectData>(action.Hash);
        if (data == null || query.Message is not { } message)
            return;

        var account = await accountService.FindByIdAsync(data.AccountId, noTracking: true);
        if (account == null)
        {
            await botClient.EditMessageText(message.Chat.Id, message.Id, "未找到指定 AI Router 账号");
            return;
        }

        await botClient.EditMessageText(
            message.Chat.Id,
            message.Id,
            $"确认删除 AI Router 账号绑定？\n账号：{AiRouterMessageExtensions.MdCode(account.Account)}",
            ParseMode.MarkdownV2,
            replyMarkup: new InlineKeyboardMarkup([
                [
                    InlineKeyboardButton.WithCallbackData(
                        "确认删除",
                        await actionManager.PutActionAsync("ai_router_delete_confirm", message.Chat.Id, query.From.Id,
                                                           new AiRouterDeleteConfirmData(account.Id, Confirm: true))),
                    InlineKeyboardButton.WithCallbackData(
                        "取消",
                        await actionManager.PutActionAsync("ai_router_delete_confirm", message.Chat.Id, query.From.Id,
                                                           new AiRouterDeleteConfirmData(account.Id, Confirm: false)))
                ]
            ]));
    }
}
