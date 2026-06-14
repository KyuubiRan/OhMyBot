using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Services.AiRouter;
using OhMyTelegramBot.Enums;
using OhMyTelegramBot.Extensions;
using OhMyTelegramBot.Interfaces;
using OhMyTelegramBot.Models.ActionData;
using OhMyTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Extensions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace OhMyTelegramBot.Commands.UserCommands.AiRouter;

[Component(Key = "cmd__ai_router_signin")]
public sealed class AiRouterSignInCommand(
    AiRouterAccountService accountService,
    AiRouterSignService signService,
    BotActionManager actionManager) : ICommand
{
    public SupportedChatType SupportChatTypes => SupportedChatType.Private;

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        var accounts = await accountService.ListAccountsAsync(senderId.ToString(), SoftwareType.Telegram, noTracking: true);
        if (accounts.Count == 0)
        {
            await botClient.SendMessage(chatId, "请先使用 /ai_router_bind 绑定 AI Router 账号", replyParameters: message);
            return;
        }

        if (accounts.Count == 1)
        {
            await SignSingleAsync(botClient, message, chatId, accounts[0]);
            return;
        }

        var buttons = new List<InlineKeyboardButton[]>();
        foreach (var account in accounts)
        {
            buttons.Add([
                InlineKeyboardButton.WithCallbackData(
                    account.Account,
                    await actionManager.PutActionAsync("ai_router_signin_select", chatId, senderId, new AiRouterSignInSelectData(account.Id)))
            ]);
        }

        await botClient.SendMessage(
            chatId,
            "请选择要签到的 AI Router 账号：",
            replyMarkup: new InlineKeyboardMarkup(buttons),
            replyParameters: message);
    }

    private async Task SignSingleAsync(ITelegramBotClient botClient, Message message, long chatId, OhMyLib.Models.AiRouter.AiRouterAccount account)
    {
        var msg = await botClient.SendMessage(
            chatId,
            $"正在签到 {AiRouterMessageExtensions.MdCode(account.Account)}，请稍候\\.\\.\\.",
            ParseMode.MarkdownV2,
            replyParameters: message);
        try
        {
            var result = await signService.SignInAsync(account);
            await botClient.EditMessageText(
                chatId,
                msg.Id,
                string.Join('\n',
                    Markdown.Escape("[AI Router-手动签到]"),
                    result.ToMarkdownV2Message(),
                    $"时间：{Markdown.Escape(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"))}"),
                ParseMode.MarkdownV2);
        }
        catch (Exception e)
        {
            await botClient.EditMessageText(
                chatId,
                msg.Id,
                string.Join('\n',
                    Markdown.Escape("[AI Router-手动签到]"),
                    $"手动签到执行失败：{Markdown.Escape(e.GetBaseException().Message)}"),
                ParseMode.MarkdownV2);
        }
    }
}
