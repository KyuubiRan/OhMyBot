using System.Net;
using System.Text.Json.Nodes;
using FoxTail.Extensions;
using OhMyLib.Attributes;
using OhMyLib.Requests.Kuro;
using OhMyLib.Services;
using OhMyTelegramBot.Enums;
using OhMyTelegramBot.Interfaces;
using OhMyTelegramBot.Models.ActionData;
using OhMyTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace OhMyTelegramBot.Commands.UserCommands.Kuro;

[Component(Key = "cmd__kuro_bind")]
public sealed class KuroBindCommand(KuroUserService kuroUserService, BotActionManager actionManager) : ICommand
{
    public SupportedChatType SupportChatTypes => SupportedChatType.Private;

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        if (args.IsEmpty)
        {
            const string argJson = """
                                   ```json
                                   {
                                      "uid": "必填，库街区UID",
                                      "token": "必填，请求头中的token字段",
                                      "devCode": "选填，建议填写，不填使用空值",
                                      "distinctId": "选填，建议填写，不填使用空值"
                                   }
                                   ```
                                   """;

            await botClient.SendMessage(
                chatId,
                "用法：/kuro_bind <data> 按照下方Json格式填入"
            );

            await botClient.SendMessage(chatId, argJson, parseMode: ParseMode.MarkdownV2);
            return;
        }

        var entity = message.Entities?.FirstOrDefault(x => x.Type == MessageEntityType.Pre);
        var json = entity != null
            ? message.Text?.Substring(entity.Offset, entity.Length)
            : message.Text?[(message.Text.IndexOf(' ') + 1)..]?.Trim();
        if (json == null)
        {
            await botClient.SendMessage(chatId, "参数错误，请检查后重新输入");
            return;
        }

        JsonNode data;
        try
        {
            data = JsonNode.Parse(json)!;
        }
        catch
        {
            await botClient.SendMessage(chatId, "参数错误，Json格式有误，请检查后重新输入");
            return;
        }

        var uid = data["uid"]?.GetValue<string>();

        if (!long.TryParse(uid, out var kuid))
        {
            await botClient.SendMessage(chatId, "参数错误，uid格式有误，请检查后重新输入");
            return;
        }

        var token = data["token"]?.GetValue<string>();
        if (token.IsWhiteSpaceOrNull)
        {
            await botClient.SendMessage(chatId, "参数错误，token不能为空，请检查后重新输入");
            return;
        }

        var devCode = data["devCode"]?.GetValue<string>();
        var distinctId = data["distinctId"]?.GetValue<string>();
        var ipAddress = data["ipAddress"]?.GetValue<string>();
        if (!ipAddress.IsWhiteSpaceOrNull && !IPAddress.TryParse(ipAddress, out _))
        {
            await botClient.SendMessage(chatId, "参数错误，ipAddress格式有误，请检查后重新输入");
        }

        var existsBinding = await kuroUserService.FindByBbsIdAsync(kuid);
        if (existsBinding != null && existsBinding.OwnerBotUser.OwnerId != senderId.ToString())
        {
            await botClient.SendMessage(chatId, "该库街区UID已被绑定，如有疑问请联系Bot管理员处理");
            return;
        }

        var msg = await botClient.SendMessage(chatId, "正在获取库街区信息，请稍候...");

        using var client = new KuroHttpClient(token, devCode, distinctId);
        var me = await client.BbsGetMineAsync(kuid);
        if (!me.Success)
        {
            await botClient.EditMessageText(chatId, msg.Id, $"获取库街区信息失败({me.Code})：{me.Msg}");
            return;
        }

        var mine = me.Data?.Mine;
        var text = """
                   用户名：{0}
                   UID：{1}
                   """.Fmt(mine?.UserName, mine?.UserId);

        await botClient.EditMessageText(chatId, msg.Id, "库街区信息获取成功，请检查是否为您的账号信息：\n" + text, replyMarkup: new InlineKeyboardMarkup
        {
            InlineKeyboard =
            [
                [
                    InlineKeyboardButton.WithCallbackData(
                        "确认绑定", await actionManager.PutActionAsync("kuro_bind", chatId, senderId,
                            new KuroBindActionData(true, kuid, token, devCode, distinctId, ipAddress))),
                    InlineKeyboardButton.WithCallbackData(
                        "取消绑定", await actionManager.PutActionAsync("kuro_bind", chatId, senderId,
                            new KuroBindActionData(false, kuid, token)))
                ]
            ]
        });
    }
}