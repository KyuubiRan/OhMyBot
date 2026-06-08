using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Services.AiRouter;
using OhMyTelegramBot.Enums;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.UserCommands.AiRouter;

[Component(Key = "cmd__ai_router_bind")]
public sealed class AiRouterBindCommand(AiRouterAccountService accountService) : ICommand
{
    public SupportedChatType SupportChatTypes => SupportedChatType.Private;

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        if (args.Length < 2)
        {
            await botClient.SendMessage(chatId, "用法：/ai_router_bind <account> <password>", replyParameters: message);
            return;
        }

        var account = args[0];
        var password = string.Join(' ', args.Skip(1));
        var msg = await botClient.SendMessage(chatId, "正在绑定 AI Router 账号，请稍候...", replyParameters: message);

        try
        {
            await accountService.BindAsync(senderId.ToString(), SoftwareType.Telegram, account, password);
            await botClient.EditMessageText(chatId, msg.Id, "绑定成功！");
        }
        catch (Exception e)
        {
            await botClient.EditMessageText(chatId, msg.Id, "绑定失败：" + e.GetBaseException().Message);
        }
    }
}
