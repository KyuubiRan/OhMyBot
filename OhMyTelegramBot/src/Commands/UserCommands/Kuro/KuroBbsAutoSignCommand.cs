using System.Text;
using FoxTail.Extensions;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Enums.Kuro;
using OhMyLib.Services;
using OhMyTelegramBot.Interfaces;
using OhMyTelegramBot.Models.ActionData;
using OhMyTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace OhMyTelegramBot.Commands.UserCommands.Kuro;

[Component(Key = "cmd__kuro_auto_sign")]
public class KuroBbsAutoSignCommand(BotUserService botUserService, BotActionManager actionManager) : ICommand
{
    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        var user = await botUserService.GetUserAsync(senderId.ToString(), SoftwareType.Telegram);
        if (user is not { KuroUser: { } ku } || ku.Token.IsWhiteSpaceOrNull)
        {
            await botClient.SendMessage(chatId, "请先绑定库街区账号后再使用自动签到功能");
            return;
        }

        var features = Enum.GetValues<KuroBbsTaskType>().Where(x => x > 0).ToList();
        var m = new StringBuilder("点击下方按钮进行开/关签到功能\n");
        m.Append("当前已启用：")
            .AppendLine(features.Where(x => (ku.BbsTask & x) != 0).Select(x => x.Name).JoinToString(' '));

        var buttons = features.Select(x =>
            InlineKeyboardButton.WithCallbackData(
                text: $"{x.Name}",
                callbackData: actionManager.PutActionAsync("kuro_auto_sign", chatId, senderId, new KuroAutoSignToggleData(ku.Id, x)).Result
            )).Chunk(2).ToList();

        buttons.Add([
            InlineKeyboardButton.WithCallbackData(
                text: "全部开启/关闭",
                callbackData: actionManager.PutActionAsync("kuro_auto_sign", chatId, senderId, new KuroAutoSignToggleData(ku.Id,
                    KuroBbsTaskType.Signin | KuroBbsTaskType.ViewPosts | KuroBbsTaskType.SharePosts | KuroBbsTaskType.LikePosts
                )).Result
            )
        ]);

        var keyboard = new InlineKeyboardMarkup(buttons);

        await botClient.SendMessage(
            chatId,
            m.ToString(),
            replyMarkup: keyboard
        );
    }
}