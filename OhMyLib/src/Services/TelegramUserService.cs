using FoxTail.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using OhMyLib.Attributes;
using OhMyLib.Dto;
using OhMyLib.Extensions;
using OhMyLib.Models.Telegram;
using OhMyLib.Repositories;

namespace OhMyLib.Services;

[Component]
public class TelegramUserService(TelegramUserRepo repo, IDistributedCache cache)
{
    private static string KeyForUserId(long id) => $"tg_user:id:{id}";
    private static string KeyForUsername(string username) => $"tg_user:username:{username}";

    public async ValueTask<TelegramUserDto> GetCachedUserByIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        return await cache.GetOrSetObjectAsync(KeyForUserId(userId), async () =>
        {
            var user = await repo.EntitySet.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken: cancellationToken);
            if (user == null)
                return new TelegramUserDto(-1, userId, null, null, null, null, null);

            if (!user.Username.IsWhiteSpaceOrNull)
                await cache.SetObjectAsync(KeyForUsername(user.Username), user.UserId, cancellationToken: cancellationToken);

            return new TelegramUserDto(
                user.Id,
                user.UserId,
                user.Username,
                user.FirstName,
                user.LastName,
                user.CreatedAt,
                user.UpdatedAt
            );
        }, token: cancellationToken);
    }

    public async ValueTask<TelegramUserDto> GetCachedUserByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var userId = await cache.GetOrSetObjectAsync(KeyForUsername(username), async () =>
        {
            var user = await repo.EntitySet.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Username == username, cancellationToken: cancellationToken);
            if (user == null)
                return -1L;

            return user.UserId;
        }, token: cancellationToken);

        if (userId <= 0L)
            return new TelegramUserDto(-1, -1, null, null, null, null, null);

        return await GetCachedUserByIdAsync(userId, cancellationToken);
    }

    public async ValueTask<bool> ExistsAsync(long userId, CancellationToken cancellationToken = default)
    {
        return await GetCachedUserByIdAsync(userId, cancellationToken) is { ExistsInDatabase: true };
    }

    public async ValueTask<TelegramUser?> GetUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        return await repo.EntitySet
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken: cancellationToken);
    }

    public async ValueTask LogUserAsync(
        long userId,
        string? username = null,
        string? firstName = null,
        string? lastName = null,
        CancellationToken cancellationToken = default
    )
    {
        var current = await GetCachedUserByIdAsync(userId, cancellationToken);
        if (current.SameAs(userId, username, firstName, lastName) && current.ExistsInDatabase)
            return;

        if (username != null)
        {
            var cached = await GetCachedUserByUsernameAsync(username, cancellationToken);
            if (cached.UserId != userId)
            {
                var user = await GetUserAsync(userId, cancellationToken);
                if (user != null && !user.Username.IsWhiteSpaceOrNull)
                {
                    user.Username = null;
                    user.UpdatedAt = DateTimeOffset.UtcNow;

                    await cache.RemoveAsync(KeyForUsername(username), cancellationToken);
                    await cache.RemoveAsync(KeyForUserId(userId), cancellationToken);
                }
            }
        }

        TelegramUser entity;
        if (current.ExistsInDatabase)
        {
            entity = await GetUserAsync(userId, cancellationToken) ?? throw new InvalidOperationException("User should exist in database.");
            entity.Username = username;
            entity.FirstName = firstName;
            entity.LastName = lastName;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            entity = new TelegramUser
            {
                UserId = userId,
                Username = username,
                FirstName = firstName,
                LastName = lastName,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await repo.AddAsync(entity, cancellationToken);
        }

        await cache.SetObjectAsync(KeyForUserId(userId), entity.ToDto(), cancellationToken: cancellationToken);
        if (!username.IsWhiteSpaceOrNull)
            await cache.SetObjectAsync(KeyForUsername(username), entity.UserId, cancellationToken: cancellationToken);

        await repo.SaveChangesAsync(cancellationToken);
    }
}