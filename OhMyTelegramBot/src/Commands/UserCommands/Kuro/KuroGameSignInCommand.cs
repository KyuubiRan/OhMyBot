using System.Text;
using FoxTail.Extensions;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Enums.Kuro;
using OhMyLib.Services;
using OhMyLib.Services.Kuro;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.UserCommands.Kuro;

[Component(Key = "cmd__kuro_game_signin")]
public sealed class KuroGameSignInCommand(BotUserService botUserService, KuroSignService kuroSignService) : ICommand
{
    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        var user = await botUserService.GetUserWithKuroAsync(senderId.ToString(), SoftwareType.Telegram, noTracking: true);
        if (user is not { KuroUser: { } ku } || ku.Token.IsWhiteSpaceOrNull || ku.BbsUserId == null)
        {
            await botClient.SendMessage(chatId, "请先绑定库街区账号后再使用初始化游戏角色功能", replyParameters: message);
            return;
        }

        var notify = args.IsNotEmpty;
        var trueTypes = new List<KuroGameType>();
        foreach (var s in args)
        {
            if (Enum.TryParse<KuroGameType>(s, true, out var t))
            {
                trueTypes.Add(t);
                continue;
            }

            await botClient.SendMessage(chatId, "参数错误，类型可选：wuwa、pgr", replyParameters: message);
            return;
        }

        if (trueTypes.IsEmpty)
        {
            trueTypes.AddRange(Enum.GetValues<KuroGameType>());
        }

        var msg = await botClient.SendMessage(chatId, "签到中...", replyParameters: message);
        try
        {
            var signResult = await kuroSignService.ExecuteGameSignAsync(ku, trueTypes, includeMissingConfigMessage: notify);
            var result = new StringBuilder();

            if (signResult.HasResult)
            {
                foreach (var line in signResult.Lines)
                    result.AppendLine(line);
            }
            else
            {
                result.AppendLine("没有可执行的游戏签到任务");
            }

            await botClient.EditMessageText(chatId, msg.MessageId, result.ToString().Trim());
        }
        catch (Exception e)
        {
            await botClient.EditMessageText(chatId, msg.MessageId, "签到过程中出现错误：" + e.GetBaseException().Message);
        }
    }
}