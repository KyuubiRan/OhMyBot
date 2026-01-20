using OhMyLib.Attributes;
using OhMyLib.CachedModels;
using OhMyLib.Enums;

namespace OhMyTelegramBot.Components;

[Component]
public class CommandContext
{
    public long ChatId { get; set; }
    public long SenderId { get; set; }
    public string Command { get; set; } = null!;
    public string[] Args { get; set; } = null!;
    public CachedBotUser User { get; set; } = null!;
    public UserPrivilege Privilege => User.Privilege;
}