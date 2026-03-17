using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Services;
using OhMyTelegramBot.Interfaces;
using OhMyTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.SuperAdminCommands;

[Component(Key = "cmd__coin")]
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public partial class CoinCommand(BotUserService userService, ILogger<CoinCommand> logger, TMessageHelperService helperService) : ICommand
{
    public UserPrivilege RequirePrivilege => UserPrivilege.SuperAdmin;

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        if (args.Length < 2)
        {
            await botClient.SendMessage(chatId, "用法: /coin add|set amount @user|id|reply", replyParameters: message);
            return;
        }

        bool isAdd;
        switch (args[0].ToLowerInvariant())
        {
            case "add":
                isAdd = true;
                break;
            case "set":
                isAdd = false;
                break;
            default:
                return;
        }

        if (!int.TryParse(args[1], out var amount))
        {
            await botClient.SendMessage(chatId, $"无法解析的数字 {args[1]}, 有效范围:{int.MinValue} ~ {int.MaxValue}");
            return;
        }

        var mentioned = await helperService.GetReplyToOrFirstMentionedUser(message);
        var id = mentioned?.Id.ToString() ?? args.LastOrDefault();
        if (!long.TryParse(id, out _))
            return;

        var target = await userService.GetUserAsync(id, SoftwareType.Telegram);
        if (target == null)
        {
            await botClient.SendMessage(chatId, "未找到指定用户", replyParameters: message);
            return;
        }

        var (oldAmount, newAmount) = await userService.UpdateCoinAsync(target.OwnerId, SoftwareType.Telegram, amount, isAdd);

        LogChanged(target.OwnerId, oldAmount, newAmount, message.From?.Id);
        await botClient.SendMessage(chatId, $"已调整哈狐币 {oldAmount} => {newAmount}", replyParameters: message);
    }

    [LoggerMessage(LogLevel.Information, "Set user(uid={uid}) coin: {oldAmount} => {newAmount} by {doerUid}")]
    partial void LogChanged(string uid, int oldAmount, int newAmount, long? doerUid);
}