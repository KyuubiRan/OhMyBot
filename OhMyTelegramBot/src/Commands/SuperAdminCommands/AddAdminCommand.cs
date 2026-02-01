using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Services;
using OhMyTelegramBot.Components;
using OhMyTelegramBot.Interfaces;
using OhMyTelegramBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace OhMyTelegramBot.Commands.SuperAdminCommands;

[Component(Key = "cmd__promote")]
public sealed class AddAdminCommand(BotUserService service, CommandContext context, TMessageHelperService helperService) : ICommand
{
    public UserPrivilege RequirePrivilege => UserPrivilege.SuperAdmin;

    public async Task OnReceiveCommand(ITelegramBotClient botClient, Message message, long chatId, long senderId, string[] args)
    {
        var mentioned = await helperService.GetReplyToOrFirstMentionedUser(message);
        var id = mentioned?.Id.ToString() ?? args.ElementAtOrDefault(0);
        if (!long.TryParse(id, out _))
            return;

        var target = await service.GetCachedUserAsync(id, SoftwareType.Telegram);

        if (target.Uid == senderId.ToString())
        {
            await botClient.SendMessage(chatId, "喵喵喵？");
            return;
        }

        if (target.Privilege >= UserPrivilege.Owner)
        {
            await botClient.SendMessage(chatId, "休要造反！");
            return;
        }

        if (context.Privilege < target.Privilege)
        {
            await botClient.SendMessage(chatId, "无法操作比自己权限更高的用户");
            return;
        }

        if ((byte)target.Privilege * 10 >= (byte)context.Privilege)
        {
            await botClient.SendMessage(chatId, $"该用户已有 {target.Privilege.ToString()} 权限");
            return;
        }

        var promotedPriv = (UserPrivilege)Math.Max(1, (byte)target.Privilege * 10);
        await service.SetPrivilegeAsync(id, SoftwareType.Telegram, promotedPriv);
        await botClient.SendMessage(chatId, $"已提升该用户权限至 {promotedPriv.ToString()}");
    }
}