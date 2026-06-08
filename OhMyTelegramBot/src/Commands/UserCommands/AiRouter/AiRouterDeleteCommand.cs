using OhMyLib.Models.AiRouter;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Services.AiRouter;
using OhMyTelegramBot.Enums;
using OhMyTelegramBot.Interfaces;
using OhMyTelegramBot.Models.ActionData;
using OhMyTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace OhMyTelegramBot.Commands.UserCommands.AiRouter;

[Component(Key = "cmd__ai_router_del")]
public sealed class AiRouterDeleteCommand(AiRouterAccountService accountService, BotActionManager actionManager) : ICommand
{
    public SupportedChatType SupportChatTypes => SupportedChatType.Private;

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        var accounts = await accountService.ListAccountsAsync(senderId.ToString(), SoftwareType.Telegram, noTracking: true);
        if (accounts.Count == 0)
        {
            await botClient.SendMessage(chatId, "尚未绑定 AI Router 账号", replyParameters: message);
            return;
        }

        await botClient.SendMessage(
            chatId,
            "请选择要删除的 AI Router 账号：",
            replyMarkup: await BuildKeyboardAsync(accounts, actionManager, chatId, senderId),
            replyParameters: message);
    }

    private static async Task<InlineKeyboardMarkup> BuildKeyboardAsync(
        IReadOnlyList<AiRouterAccount> accounts,
        BotActionManager actionManager,
        long chatId,
        long senderId)
    {
        var buttons = new List<InlineKeyboardButton[]>();
        foreach (var account in accounts)
        {
            buttons.Add([
                InlineKeyboardButton.WithCallbackData(
                    account.Account,
                    await actionManager.PutActionAsync("ai_router_delete_select", chatId, senderId, new AiRouterDeleteSelectData(account.Id)))
            ]);
        }

        return new InlineKeyboardMarkup(buttons);
    }
}
