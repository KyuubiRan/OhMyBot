using System.Text;
using FoxTail.Extensions;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Models.AiRouter;
using OhMyLib.Services.AiRouter;
using OhMyTelegramBot.Enums;
using OhMyTelegramBot.Interfaces;
using OhMyTelegramBot.Models.ActionData;
using OhMyTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace OhMyTelegramBot.Commands.UserCommands.AiRouter;

[Component(Key = "cmd__ai_router_auto_signin")]
public sealed class AiRouterAutoSignInCommand(AiRouterAccountService accountService, BotActionManager actionManager) : ICommand
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

        await botClient.SendMessage(
            chatId,
            BuildPanelText(accounts),
            replyMarkup: await BuildKeyboardAsync(accounts, actionManager, chatId, senderId),
            replyParameters: message);
    }

    public static string BuildPanelText(IReadOnlyList<AiRouterAccount> accounts)
    {
        var sb = new StringBuilder("点击下方按钮进行开/关签到功能\n");
        sb.Append("当前已启用：")
          .Append(accounts.Count(x => x.AutoSignEnabled))
          .Append('/')
          .AppendLine(accounts.Count.ToString());
        return sb.ToString();
    }

    public static async Task<InlineKeyboardMarkup> BuildKeyboardAsync(
        IReadOnlyList<AiRouterAccount> accounts,
        BotActionManager actionManager,
        long chatId,
        long senderId)
    {
        var buttons = new List<InlineKeyboardButton[]>();
        var row = new List<InlineKeyboardButton>();

        foreach (var account in accounts)
        {
            row.Add(InlineKeyboardButton.WithCallbackData(
                $"{(account.AutoSignEnabled ? "[开]" : "[关]")} {account.Account}",
                await actionManager.PutActionAsync("ai_router_auto_sign", chatId, senderId, new AiRouterAutoSignToggleData(account.Id))));

            if (row.Count == 2)
            {
                buttons.Add(row.ToArray());
                row = [];
            }
        }

        if (row.Count > 0)
            buttons.Add(row.ToArray());

        buttons.Add([
            InlineKeyboardButton.WithCallbackData(
                "全部开启/关闭",
                await actionManager.PutActionAsync("ai_router_auto_sign", chatId, senderId, new AiRouterAutoSignToggleData(0, ToggleAll: true)))
        ]);

        return new InlineKeyboardMarkup(buttons);
    }
}
