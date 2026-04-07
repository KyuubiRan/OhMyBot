using System.Text;
using FoxTail.Extensions;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Services;
using OhMyLib.Services.Kuro;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.UserCommands.Kuro;

[Component(Key = "cmd__kuro_signin")]
public sealed class KuroBbsSignInCommand(BotUserService userService, KuroSignService kuroSignService) : ICommand
{
    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        var botUser = await userService.GetUserWithKuroAsync(senderId.ToString(), SoftwareType.Telegram, noTracking: true);
        var kUser = botUser?.KuroUser;
        if (kUser == null)
        {
            await botClient.SendMessage(chatId, "请先绑定库街区账号后再使用签到功能", replyParameters: message);
            return;
        }

        if (kUser.Token.IsWhiteSpaceOrNull)
        {
            await botClient.SendMessage(chatId, "绑定的库街区账号信息不完整，请重新绑定后再使用签到功能", replyParameters: message);
            return;
        }

        var msg = await botClient.SendMessage(chatId, "签到中，请稍候...", replyParameters: message);

        try
        {
            var result = await kuroSignService.ExecuteBbsSignAsync(
                             kUser,
                             kUser.BbsTask,
                             args,
                             runAllWhenNoRequestedActions: true,
                             onProgress: async (progress, cancellationToken) =>
                             {
                                 var progressText = new StringBuilder("签到中，请稍候...\n");
                                 progressText.AppendLine("当前任务进度：");
                                 foreach (var task in progress)
                                 {
                                     progressText.AppendLine($"- {task.Remark}: {task.CompleteTimes}/{task.NeedActionTimes} (+{task.GainGold})");
                                 }

                                 await botClient.EditMessageText(chatId, msg.MessageId, progressText.ToString(), cancellationToken: cancellationToken);
                             });

            var resultMessage = new StringBuilder("签到结果：\n");
            if (result.HasResult)
            {
                foreach (var line in result.Lines)
                    resultMessage.AppendLine(line);
            }
            else
            {
                resultMessage.AppendLine("没有可执行的社区任务");
            }

            resultMessage.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            await botClient.EditMessageText(chatId, msg.MessageId, resultMessage.ToString().Trim());
        }
        catch (Exception e)
        {
            await botClient.EditMessageText(chatId, msg.MessageId, "签到过程中出现错误：" + e.GetBaseException().Message);
        }
    }
}