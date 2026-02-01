using System.Text;
using FoxTail.Extensions;
using OhMyLib.Attributes;
using OhMyLib.Enums.Kuro;
using OhMyLib.Services;
using OhMyTelegramBot.Interfaces;
using OhMyTelegramBot.Models;
using OhMyTelegramBot.Models.ActionData;
using OhMyTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Actions;

[Component(Key = "action__kuro_auto_sign")]
public class KuroAutoSignToggleAction(BotActionManager actionManager, KuroUserService kuroUserService) : IBotActionHandler
{
    public async Task OnReceiveAction(ITelegramBotClient botClient, CallbackQuery query, BotAction action)
    {
        var data = await actionManager.GetActionDataAsync<KuroAutoSignToggleData>(action.Hash);
        if (data == null)
            return;

        var ku = await kuroUserService.FindByIdAsync(data.Kid);
        if (ku == null)
            return;

        ku.BbsTask ^= data.Tasks;
        await kuroUserService.UpdateAsync(ku);

        if (query.Message is not { } m)
            return;

        var features = Enum.GetValues<KuroBbsTaskType>().Where(x => x > 0).ToList();
        var msg = new StringBuilder("点击下方按钮进行开/关签到功能\n");
        msg.Append("当前已启用：")
            .AppendLine(features.Where(x => (ku.BbsTask & x) != 0).Select(x => x.Name).JoinToString(' '));

        await botClient.EditMessageText(
            m.Chat.Id,
            m.Id,
            msg.ToString(),
            replyMarkup: m.ReplyMarkup);
    }
}