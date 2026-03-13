using System.Text;
using FoxTail.Extensions;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Enums.Kuro;
using OhMyLib.Services;
using OhMyLib.Utils;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.UserCommands.Kuro;

[Component(Key = "cmd__kuro_game_auto_signin")]
public sealed class KuroGameAutoSignInCommand(BotUserService botUserService, KuroUserService kuroUserService) : ICommand
{
    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        if (args.Length < 1)
        {
            await botClient.SendMessage(chatId, "用法：/kuro_game_auto_signin <on/off> [类型]，类型可选：wuwa、pgr，留空为全部");
            return;
        }

        var onOff = args[0].ToLowerInvariant();
        if (!BoolUtils.TryParse(onOff, out var onOffBool))
        {
            await botClient.SendMessage(chatId, "参数错误，第一参数应为 on 或 off");
            return;
        }

        var user = await botUserService.GetUserAsync(senderId.ToString(), SoftwareType.Telegram);
        if (user is not { KuroUser: { } ku } || ku.Token.IsWhiteSpaceOrNull || ku.BbsUserId == null)
        {
            await botClient.SendMessage(chatId, "请先绑定库街区账号后再使用库街区游戏自动签到功能，若已绑定，请使用 /kuro_game_init_char 初始化游戏角色信息");
            return;
        }

        var type = args.Length > 1 ? args[1] : null;
        if (type.IsWhiteSpaceOrNull)
        {
            if (ku.GameConfigs.IsEmpty)
            {
                await botClient.SendMessage(chatId, "未找到游戏角色信息，请使用 /kuro_game_init_char 初始化游戏角色信息");
                return;
            }

            var sb = new StringBuilder();
            foreach (var config in ku.GameConfigs)
            {
                config.TaskType = onOffBool.Value ? config.TaskType | KuroGameTaskType.Signin : config.TaskType & ~KuroGameTaskType.Signin;
                sb.AppendLine(config.GameType.Name + " 自动签到已" + (onOffBool.Value ? "开启" : "关闭"));
            }

            await kuroUserService.SaveAsync();
            await botClient.SendMessage(chatId, sb.ToString());
        }
        else if (Enum.TryParse<KuroGameType>(type, true, out var t))
        {
            var config = user.KuroUser.GameConfigs.FirstOrDefault(x => x.GameType == t);
            if (config == null)
            {
                await botClient.SendMessage(chatId, $"未找到游戏类型 {t.Name} 的角色信息，请使用 /kuro_game_init_char 初始化游戏角色信息");
                return;
            }

            config.TaskType = onOffBool.Value ? config.TaskType | KuroGameTaskType.Signin : config.TaskType & ~KuroGameTaskType.Signin;

            await kuroUserService.SaveAsync();
            await botClient.SendMessage(chatId, $"{t.Name} 自动签到已{(onOffBool.Value ? "开启" : "关闭")}");
            return;
        }
        else
        {
            await botClient.SendMessage(chatId, "参数错误，类型可选：wuwa、pgr");
            return;
        }
    }
}