using System.Diagnostics.CodeAnalysis;
using OhMyLib.Attributes;
using OhMyLib.Dto;
using OhMyLib.Enums;
using Telegram.Bot.Types.Enums;

namespace OhMyTelegramBot.Components;

[Component]
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class CommandContext
{
    public ChatType ChatType { get; set; }
    public long ChatId { get; set; }
    public long SenderId { get; set; }
    public string Command { get; set; } = null!;
    public string[] Args { get; set; } = null!;
    public BotUserDto UserDto { get; set; } = null!;
    public UserPrivilege Privilege => UserDto.Privilege;
}