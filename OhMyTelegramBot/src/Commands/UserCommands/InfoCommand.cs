using System.Text;
using FoxTail.Extensions;
using OhMyLib.Attributes;
using OhMyLib.Dto;
using OhMyLib.Enums;
using OhMyLib.Services;
using OhMyTelegramBot.Extensions;
using OhMyTelegramBot.Interfaces;
using OhMyTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace OhMyTelegramBot.Commands.UserCommands;

[Component(Key = "cmd__info")]
public class InfoCommand(TelegramUserService tUserService, BotUserService bUserService, TMessageHelperService helperService) : ICommand
{
    private static string UserToText(User user, BotUserDto botUserDto)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ID: `{user.Id}`");
        sb.AppendLine($"昵称: `{user.FirstName} {user.LastName}`");
        sb.AppendLine($"用户名: {(string.IsNullOrEmpty(user.Username) ? "无" : "@" + user.Username)}");
        sb.AppendLine($"权限: `{botUserDto.Privilege}`");
        return sb.ToString();
    }

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        if (args.IsEmpty && message.ReplyToMessage == null)
        {
            var self = await tUserService.GetCachedUserByIdAsync(senderId);
            var selfBotUser = await bUserService.GetCachedUserAsync(senderId.ToString(), SoftwareType.Telegram);
            var text = UserToText(self.ToUser(), selfBotUser);
            await botClient.SendMessage(chatId, text, ParseMode.MarkdownV2);
            return;
        }

        var mentioned = await helperService.GetReplyToOrFirstMentionedUser(message);
        if (mentioned == null)
        {
            await botClient.SendMessage(chatId, "未找到指定用户");
            return;
        }

        var target = await tUserService.GetCachedUserByIdAsync(mentioned.Id);
        var targetBotUser = await bUserService.GetCachedUserAsync(mentioned.Id.ToString(), SoftwareType.Telegram);
        var targetText = UserToText(target.ToUser(), targetBotUser);
        await botClient.SendMessage(chatId, targetText, ParseMode.MarkdownV2);
    }
}