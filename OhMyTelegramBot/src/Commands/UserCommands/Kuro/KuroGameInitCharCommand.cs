using System.Text;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Services.Kuro;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.UserCommands.Kuro;

[Component(Key = "cmd__kuro_game_init_char")]
public sealed class KuroGameInitCharCommand(KuroAccountService kuroAccountService) : ICommand
{
    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        try
        {
            var result = await kuroAccountService.InitializeGameCharactersAsync(senderId.ToString(), SoftwareType.Telegram);
            if (result.IsEmpty)
            {
                await botClient.SendMessage(chatId, "角色列表为空", replyParameters: message);
                return;
            }

            var msg = new StringBuilder("初始化游戏角色信息完成\n");
            foreach (var item in result.Characters)
            {
                msg.Append('[')
                   .Append(item.ServerName)
                   .AppendLine("]")
                   .AppendLine("UID: " + item.RoleId)
                   .AppendLine("昵称: " + item.RoleName)
                   .AppendLine("等级: " + item.GameLevel)
                   .AppendLine("活跃天数: " + item.ActiveDay);
            }

            await botClient.SendMessage(chatId, msg.ToString(), replyParameters: message);
        }
        catch (Exception e)
        {
            await botClient.SendMessage(chatId, e.GetBaseException().Message, replyParameters: message);
        }
    }
}