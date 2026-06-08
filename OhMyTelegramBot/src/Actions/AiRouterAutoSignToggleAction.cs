using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Services.AiRouter;
using OhMyTelegramBot.Commands.UserCommands.AiRouter;
using OhMyTelegramBot.Interfaces.Handlers;
using OhMyTelegramBot.Models;
using OhMyTelegramBot.Models.ActionData;
using OhMyTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Actions;

[Component(Key = "action__ai_router_auto_sign")]
public sealed class AiRouterAutoSignToggleAction(BotActionManager actionManager, AiRouterAccountService accountService) : IBotCallbackActionHandler
{
    public async Task OnReceiveAction(ITelegramBotClient botClient, CallbackQuery query, BotAction action)
    {
        var data = await actionManager.GetActionDataAsync<AiRouterAutoSignToggleData>(action.Hash);
        if (data == null || query.Message is not { } message)
            return;

        var ownerId = query.From.Id.ToString();
        var accounts = data.ToggleAll
                           ? await accountService.ToggleAllAutoSignAsync(ownerId, SoftwareType.Telegram)
                           : await accountService.ToggleAutoSignAsync(ownerId, SoftwareType.Telegram, data.AccountId);

        if (accounts.Count == 0)
        {
            await botClient.EditMessageText(message.Chat.Id, message.Id, "未找到 AI Router 账号");
            return;
        }

        await botClient.EditMessageText(
            message.Chat.Id,
            message.Id,
            AiRouterAutoSignInCommand.BuildPanelText(accounts),
            replyMarkup: await AiRouterAutoSignInCommand.BuildKeyboardAsync(accounts, actionManager, message.Chat.Id, query.From.Id));
    }
}
