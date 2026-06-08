using System.Text;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Services.AiRouter;
using OhMyTelegramBot.Enums;
using OhMyTelegramBot.Extensions;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace OhMyTelegramBot.Commands.UserCommands.AiRouter;

[Component(Key = "cmd__ai_router_list")]
public sealed class AiRouterListCommand(AiRouterAccountService accountService) : ICommand
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

        var sb = new StringBuilder("\\[AI Router\\]\n已绑定账号：\n");
        foreach (var account in accounts)
            sb.AppendLine($"\\- {AiRouterMessageExtensions.MdCode(account.Account)}：自动签到{(account.AutoSignEnabled ? "开启" : "关闭")}");

        await botClient.SendMessage(chatId, sb.ToString(), ParseMode.MarkdownV2, replyParameters: message);
    }
}
