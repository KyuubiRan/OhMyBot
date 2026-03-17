using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using OhMyLib.Attributes;
using OhMyLib.Dto;
using OhMyLib.Enums;
using OhMyLib.Extensions;
using OhMyLib.Models.Common;
using OhMyLib.Repositories;

namespace OhMyLib.Services;

[Component]
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class BotUserService(BotUserRepo repo, IDistributedCache cache)
{
    private static string KeyForUser(string id, SoftwareType type) => $"bot_user:{type}:{id}";

    public async Task InvalidateCacheAsync(string id, SoftwareType type) => await cache.RemoveAsync(KeyForUser(id, type));

    public async ValueTask<BotUser?> GetUserAsync(string id, SoftwareType type, CancellationToken cancellationToken = default)
    {
        return await repo.EntitySet
                         .FirstOrDefaultAsync(x => x.OwnerId == id && x.OwnerType == type, cancellationToken: cancellationToken);
    }

    public async Task<(int, int)> UpdateCoinAsync(string id, SoftwareType type, int amount, bool isAdd = true, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(id, type, cancellationToken);
        if (user == null)
        {
            return (-1, -1);
        }

        var oldCoin = user.Coin;
        user.Coin = isAdd ? user.Coin + amount : amount;
        await SaveAsync(cancellationToken);

        await cache.RemoveAsync(KeyForUser(id, type), cancellationToken);

        return (oldCoin, user.Coin);
    }

    public async ValueTask<BotUserDto> GetCachedUserAsync(string id, SoftwareType type, CancellationToken cancellationToken = default)
    {
        return await cache.GetOrSetObjectAsync(KeyForUser(id, type), async () =>
        {
            var user = await repo.EntitySet.AsNoTracking()
                                 .FirstOrDefaultAsync(x => x.OwnerId == id && x.OwnerType == type, cancellationToken: cancellationToken);
            if (user == null)
                return new BotUserDto(-1, id, type, UserPrivilege.None, 0);

            return new BotUserDto(
                user.Id,
                user.OwnerId,
                user.OwnerType,
                user.Privilege,
                user.Coin
            );
        }, token: cancellationToken);
    }

    public async ValueTask<bool> ExistsAsync(string id, SoftwareType type, CancellationToken cancellationToken = default)
    {
        var cachedUser = await GetCachedUserAsync(id, type, cancellationToken);
        return cachedUser is { Id: > 0 };
    }

    public async ValueTask<BotUser?> CreateUserIfNotExistsAsync(string id, SoftwareType type, UserPrivilege privilege = UserPrivilege.User,
                                                                CancellationToken cancellationToken = default)
    {
        if (await ExistsAsync(id, type, cancellationToken))
            return null;

        var user = new BotUser
        {
            OwnerId = id,
            OwnerType = type,
            Privilege = privilege,
            CreateAt = DateTimeOffset.UtcNow
        };

        await repo.AddAsync(user, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);
        await cache.SetObjectAsync(KeyForUser(id, type), user.ToDto(), cancellationToken: cancellationToken);

        return user;
    }

    public async ValueTask<List<BotUser>> GetAvailableUsersAsync(SoftwareType type, int offset = 0, int limit = 20,
                                                                 CancellationToken cancellationToken = default)
    {
        return await repo.EntitySet
                         .Where(x => x.OwnerType == type && x.Privilege > UserPrivilege.None)
                         .Where(x => x.KuroUser != null && x.KuroUser.Token != null)
                         .Skip(offset)
                         .Take(limit)
                         .ToListAsync(cancellationToken: cancellationToken);
    }

    public async ValueTask<BotUser> SetPrivilegeAsync(string id, SoftwareType type, UserPrivilege privilege, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(id, type, cancellationToken);
        if (user == null)
        {
            user = new BotUser
            {
                OwnerId = id,
                OwnerType = type,
                Privilege = privilege,
                CreateAt = DateTimeOffset.UtcNow
            };

            await repo.AddAsync(user, cancellationToken);
        }
        else
        {
            user.Privilege = privilege;
        }

        await repo.SaveChangesAsync(cancellationToken);

        await cache.SetObjectAsync(KeyForUser(id, type), user.ToDto(), cancellationToken: cancellationToken);

        return user;
    }

    public async ValueTask<int> SaveAsync(CancellationToken cancellationToken = default)
    {
        return await repo.SaveChangesAsync(cancellationToken);
    }
}