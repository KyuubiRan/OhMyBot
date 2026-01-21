using OhMyLib.Enums;

namespace OhMyLib.CachedModels;

public record CachedBotUser(
    long Id,
    string Uid,
    SoftwareType Type,
    UserPrivilege Privilege
)
{
    public bool ExistsInDatabase => Id > 0;
}