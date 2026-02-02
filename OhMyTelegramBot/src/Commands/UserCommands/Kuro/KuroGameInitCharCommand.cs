using System.Text;
using FoxTail.Extensions;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Enums.Kuro;
using OhMyLib.Models.Kuro;
using OhMyLib.Requests.Kuro;
using OhMyLib.Services;
using OhMyTelegramBot.Interfaces;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.UserCommands.Kuro;

[Component(Key = "cmd__kuro_game_init_char")]
public sealed class KuroGameInitCharCommand(BotUserService botUserService) : ICommand
{
    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        var user = await botUserService.GetUserAsync(senderId.ToString(), SoftwareType.Telegram);
        if (user is not { KuroUser: { } ku } || ku.Token.IsWhiteSpaceOrNull || ku.BbsUserId == null)
        {
            await botClient.SendMessage(chatId, "请先绑定库街区账号后再使用初始化游戏角色功能");
            return;
        }

        using var kuroHttpClient = new KuroHttpClient(ku);

        var defaults = await kuroHttpClient.BbsGetDefaultRoleAsync(ku.BbsUserId.Value);

        if (defaults.Code == 220)
        {
            ku.Invalidate();
            await botUserService.SaveAsync();

            throw new InvalidOperationException("Token已失效，请重新绑定库街区账号后再使用签到功能");
        }

        if (!defaults.Success || defaults.Data == null)
        {
            await botClient.SendMessage(chatId, "获取默认角色信息失败：" + defaults.Msg);
            return;
        }

        var results = defaults.Data;
        if (results.DefaultRoleList.IsEmpty)
        {
            await botClient.SendMessage(chatId, "角色列表为空");
            return;
        }

        var msg = new StringBuilder("初始化游戏角色信息完成\n");

        foreach (var result in results.DefaultRoleList)
        {
            var has = ku.GameConfigs.FirstOrDefault(x => (int)x.GameType == result.GameId);
            if (has == null)
            {
                has = new KuroGameConfig
                {
                    KuroUser = ku,
                    GameType = (KuroGameType)result.GameId,
                    GameCharacterUid = long.Parse(result.RoleId)
                };

                ku.GameConfigs.Add(has);
            }
            else
            {
                has.GameCharacterUid = long.Parse(result.RoleId);
            }

            await botUserService.SaveAsync();

            msg.Append('[')
               .Append(result.ServerName)
               .AppendLine("]")
               .AppendLine("UID: " + result.RoleId)
               .AppendLine("昵称: " + result.RoleName)
               .AppendLine("等级: " + result.GameLevel)
               .AppendLine("活跃天数: " + result.ActiveDay);
        }

        await botUserService.SaveAsync();
        await botClient.SendMessage(chatId, msg.ToString());
    }
}