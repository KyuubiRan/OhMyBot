using OhMyLib.Enums;
using OhMyLib.Models.Common;

namespace OhMyLib.Dto;

public record BotUserDto(
    long Id,
    string Uid,
    SoftwareType Type,
    UserPrivilege Privilege
)
{
    public bool ExistsInDatabase => Id > 0;
}

public static class BotUserExtensions
{
    extension(BotUser botUser)
    {
        public BotUserDto ToDto() => new(
            Id: botUser.Id,
            Uid: botUser.OwnerId,
            Type: botUser.OwnerType,
            Privilege: botUser.Privilege
        );
    }
}