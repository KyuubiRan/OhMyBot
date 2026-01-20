using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using OhMyLib.Attributes;
using OhMyLib.CachedModels;
using OhMyLib.Enums;
using OhMyLib.Extensions;
using OhMyLib.Models.Common;
using OhMyLib.Repositories;

namespace OhMyLib.Services;

[Component]
public class BotUserService(BotUserRepo repo, IDistributedCache cache)
{
    private static string KeyForUser(string id, SoftwareType type) => $"bot_user:{type}:{id}";

    public BotUser? GetUser(string id, SoftwareType type)
    {
        return repo.EntitySet
            .FirstOrDefault(x => x.OwnerId == id && x.OwnerType == type);
    }

    public CachedBotUser GetCachedUser(string id, SoftwareType type)
    {
        return cache.GetOrSetObject(KeyForUser(id, type), () =>
        {
            var user = repo.EntitySet.AsNoTracking().FirstOrDefault(x => x.OwnerId == id && x.OwnerType == type);
            if (user == null)
                return new CachedBotUser(-1, id, type, UserPrivilege.None);

            return new CachedBotUser(
                user.Id,
                user.OwnerId,
                user.OwnerType,
                user.Privilege
            );
        });
    }

    public bool Exists(string id, SoftwareType type)
    {
        return GetCachedUser(id, type).Id > 0;
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

        cache.SetObject(KeyForUser(id, type), new CachedBotUser(user.Id, user.OwnerId, user.OwnerType, user.Privilege));

        return user;
    }
}