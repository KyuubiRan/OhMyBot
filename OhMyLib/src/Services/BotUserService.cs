using Microsoft.EntityFrameworkCore;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Models.Common;
using OhMyLib.Repositories;

namespace OhMyLib.Services;

[Component]
public class BotUserService(BotUserRepo repo)
{
    public BotUser? GetUser(string id, SoftwareType type)
    {
        return repo.EntitySet
                   .FirstOrDefault(x => x.OwnerId == id && x.OwnerType == type);
    }

    public bool Exists(string id, SoftwareType type)
    {
        return repo.EntitySet
                   .AsNoTracking()
                   .Any(x => x.OwnerId == id && x.OwnerType == type);
    }

    public BotUser? CreateUserIfNotExists(string id, SoftwareType type, UserPrivilege privilege = UserPrivilege.User)
    {
        if (Exists(id, type))
            return null;

        var user = new BotUser
        {
            OwnerId = id,
            OwnerType = type,
            Privilege = privilege,
            CreateAt = DateTimeOffset.UtcNow
        };

        repo.Add(user);
        repo.SaveChanges();
        return user;
    }
}