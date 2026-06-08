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

namespace OhMyTelegramBot.Actions;

[Component(Key = "action__ai_router_signin_select")]
public sealed class AiRouterSignInSelectAction(
    BotActionManager actionManager,
    AiRouterAccountService accountService,
    AiRouterSignService signService) : IBotCallbackActionHandler
{
    public async Task OnReceiveAction(ITelegramBotClient botClient, CallbackQuery query, BotAction action)
    {
        var data = await actionManager.GetActionDataAsync<AiRouterSignInSelectData>(action.Hash);
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
            $"正在签到 {AiRouterMessageExtensions.MdCode(account.Account)}，请稍候\\.\\.\\.",
            ParseMode.MarkdownV2);

        try
        {
            var result = await signService.SignInAsync(account);
            await botClient.EditMessageText(
                message.Chat.Id,
                message.Id,
                "\\[AI Router\\]\n签到结果：\n" + result.ToMarkdownV2Message(),
                ParseMode.MarkdownV2);
        }
        catch (Exception e)
        {
            await botClient.EditMessageText(message.Chat.Id, message.Id, "签到过程中出现错误：" + e.GetBaseException().Message);
        }
    }
}
