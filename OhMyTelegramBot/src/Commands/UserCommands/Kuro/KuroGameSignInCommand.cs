using System.Text;
using FoxTail.Extensions;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Enums.Kuro;
using OhMyLib.Requests.Kuro;
using OhMyLib.Services;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.UserCommands.Kuro;

[Component(Key = "cmd__kuro_game_signin")]
public sealed class KuroGameSignInCommand(BotUserService botUserService) : ICommand
{
    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        var user = await botUserService.GetUserAsync(senderId.ToString(), SoftwareType.Telegram);
        if (user is not { KuroUser: { } ku } || ku.Token.IsWhiteSpaceOrNull || ku.BbsUserId == null)
        {
            await botClient.SendMessage(chatId, "请先绑定库街区账号后再使用初始化游戏角色功能");
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

            await botClient.SendMessage(chatId, "参数错误，类型可选：wuwa、pgr");
            return;
        }

        if (trueTypes.IsEmpty)
        {
            trueTypes.AddRange(Enum.GetValues<KuroGameType>());
        }

        var msg = await botClient.SendMessage(chatId, "签到中...");

        using var kuroHttpClient = new KuroHttpClient(ku);

        var result = new StringBuilder();

        foreach (var gameType in trueTypes)
        {
            var config = ku.GameConfigs.FirstOrDefault(x => x.GameType == gameType);

            if (config == null)
            {
                if (notify)
                    result.AppendLine($"未找到 {gameType.Name} 角色信息，请使用 /kuro_game_init_char 初始化游戏角色信息");
                continue;
            }

            var init = await kuroHttpClient.GameSignInInitAsync((int)gameType, gameType.ServerId, config.GameCharacterUid,
                ku.BbsUserId.Value);

            if (init.Code == 220)
            {
                ku.Invalidate();
                await botUserService.SaveAsync();
                    
                throw new InvalidOperationException("Token已失效，请重新绑定库街区账号后再使用签到功能");
            }

            
            if (!init.Success || init.Data is not { } signData)
            {
                await botClient.EditMessageText(chatId, msg.MessageId, $"初始化 {gameType.Name} 签到信息失败：{init.Msg}");
                return;
            }

            result.Append('[')
                  .Append(gameType.Name)
                  .AppendLine("]");

            if (signData.IsSigIn)
            {
                result
                    .AppendLine($"签到结果：今日已签到")
                    .AppendLine("签到天数：" + signData.SigInNum)
                    .AppendLine($"奖励：{GetSignInReward(signData.SigInNum)}");
                continue;
            }

            await Task.Delay(Random.Shared.Next(1000, 3000));

            var signInResult =
                await kuroHttpClient.GameSignInAsync((int)gameType, gameType.ServerId, config.GameCharacterUid, ku.BbsUserId.Value);

            result.AppendLine($"签到结果：{(signInResult.Success ? "成功" : "失败：" + signInResult.Msg)}")
                .AppendLine("签到天数：" + (signInResult.Success ? signData.SigInNum + 1 : signData.SigInNum));

            if (signInResult.Success)
            {
                result.AppendLine($"奖励：{GetSignInReward(signData.SigInNum + 1)}");
            }

            await Task.Delay(Random.Shared.Next(1000, 3000));
            continue;

            string GetSignInReward(int day)
            {
                return signData.SignInGoodsConfigs.Where(x => x.SerialNum == day - 1)
                               .Select(x => $"{x.GoodsName} x{x.GoodsNum}")
                               .JoinToString(", ");
            }
        }

        await botClient.EditMessageText(chatId, msg.MessageId, result.ToString());
    }
}