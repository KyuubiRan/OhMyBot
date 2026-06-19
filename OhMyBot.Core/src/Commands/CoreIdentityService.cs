using Microsoft.EntityFrameworkCore;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Data;
using OhMyBot.Core.Data.Entities;
using OhMyBot.Core.Identity;

namespace OhMyBot.Core.Commands;

public sealed class CoreIdentityService(
    OhMyBotV2DbContext dbContext,
    IIdentityCache identityCache,
    TimeProvider timeProvider)
{
    public async Task<ResolvedIdentity> ResolveIdentityAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var cached = await identityCache.GetAsync(request.Platform, request.UserId, cancellationToken);
        if (cached is not null)
        {
            return new ResolvedIdentity(cached.CoreUserId, cached.Privilege, request.Platform, request.UserId);
        }

        var identity = await EnsureIdentityAsync(request, cancellationToken);
        return new ResolvedIdentity(identity.CoreUserId, identity.CoreUser.Privilege, identity.Platform, identity.PlatformUserId);
    }

    public async Task<PlatformIdentity> EnsureIdentityAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var now = timeProvider.GetUtcNow();
        var identity = await dbContext.PlatformIdentities
            .Include(item => item.CoreUser)
            .FirstOrDefaultAsync(
                item => item.Platform == request.Platform && item.PlatformUserId == request.UserId,
                cancellationToken);

        if (identity is not null)
        {
            identity.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? identity.DisplayName : request.DisplayName;
            identity.Username = string.IsNullOrWhiteSpace(request.Username) ? identity.Username : request.Username;
            identity.UpdatedAt = now;
            identity.CoreUser.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            await CacheIdentityAsync(identity, cancellationToken);
            return identity;
        }

        var user = new CoreUser
        {
            CreatedAt = now,
            UpdatedAt = now
        };

        identity = new PlatformIdentity
        {
            CoreUser = user,
            Platform = request.Platform,
            PlatformUserId = request.UserId,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName,
            Username = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.CoreUsers.Add(user);
        dbContext.PlatformIdentities.Add(identity);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CacheIdentityAsync(identity, cancellationToken);
        return identity;
    }

    public async Task CacheUserIdentitiesAsync(CoreUser user, CancellationToken cancellationToken = default)
    {
        foreach (var identity in user.Identities)
        {
            await identityCache.SetAsync(
                identity.Platform,
                identity.PlatformUserId,
                new CachedIdentity(user.Id, user.Privilege),
                cancellationToken);
        }
    }

    private Task CacheIdentityAsync(PlatformIdentity identity, CancellationToken cancellationToken)
    {
        return identityCache.SetAsync(
            identity.Platform,
            identity.PlatformUserId,
            new CachedIdentity(identity.CoreUserId, identity.CoreUser.Privilege),
            cancellationToken);
    }

    private static void ValidateRequest(CommandRequest request)
    {
        if (request.Platform is BotPlatform.Unspecified)
        {
            throw new InvalidOperationException("Platform is required.");
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new InvalidOperationException("UserId is required.");
        }
    }
}
