using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Services.AiRouter;
using OhMyTelegramBot.Extensions;
using OhMyTelegramBot.Interfaces.Handlers;
using OhMyTelegramBot.Models;
using OhMyTelegramBot.Models.ActionData;
using OhMyTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace OhMyTelegramBot.Actions;

[Component(Key = "action__ai_router_delete_confirm")]
public sealed class AiRouterDeleteConfirmAction(BotActionManager actionManager, AiRouterAccountService accountService) : IBotCallbackActionHandler
{
    public async Task OnReceiveAction(ITelegramBotClient botClient, CallbackQuery query, BotAction action)
    {
        var data = await actionManager.GetActionDataAsync<AiRouterDeleteConfirmData>(action.Hash);
        if (data == null || query.Message is not { } message)
            return;

        if (!data.Confirm)
        {
            await botClient.EditMessageText(message.Chat.Id, message.Id, "删除操作已取消");
            return;
        }

        var account = await accountService.FindByIdAsync(data.AccountId, noTracking: true);
        if (account == null)
        {
            await botClient.EditMessageText(message.Chat.Id, message.Id, "未找到指定 AI Router 账号");
            return;
        }

        var deleted = await accountService.DeleteAsync(query.From.Id.ToString(), SoftwareType.Telegram, account.Account);
        await botClient.EditMessageText(
            message.Chat.Id,
            message.Id,
            deleted ? $"已删除 AI Router 账号绑定：{AiRouterMessageExtensions.MdCode(account.Account)}" : "未找到指定 AI Router 账号",
            ParseMode.MarkdownV2);
    }
}
