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

        var profile = await EnsureIdentityAsync(request, cancellationToken);
        return new ResolvedIdentity(profile.CoreUserId!.Value, profile.CoreUser!.Privilege, profile.Platform, profile.Uid);
    }

    public async Task<PlatformUserProfile> EnsureIdentityAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var now = timeProvider.GetUtcNow();
        var profile = await dbContext.PlatformUserProfiles
            .Include(item => item.CoreUser)
            .FirstOrDefaultAsync(
                item => item.Platform == request.Platform && item.Uid == request.UserId,
                cancellationToken);

        if (profile is not null)
        {
            profile.Username = string.IsNullOrWhiteSpace(request.Username) ? profile.Username : request.Username;
            profile.FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? profile.FirstName : request.FirstName;
            profile.LastName = string.IsNullOrWhiteSpace(request.LastName) ? profile.LastName : request.LastName;
            profile.Nickname = string.IsNullOrWhiteSpace(request.Nickname) ? profile.Nickname : request.Nickname;
            profile.UpdatedAt = now;
            if (profile.CoreUser is null)
            {
                profile.CoreUser = new CoreUser
                {
                    CreatedAt = now,
                    UpdatedAt = now
                };
            }
            else
            {
                profile.CoreUser.UpdatedAt = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await CacheIdentityAsync(profile, cancellationToken);
            return profile;
        }

        var user = new CoreUser
        {
            CreatedAt = now,
            UpdatedAt = now
        };

        profile = new PlatformUserProfile
        {
            CoreUser = user,
            Platform = request.Platform,
            Uid = request.UserId,
            Username = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username,
            FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? null : request.FirstName,
            LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName,
            Nickname = string.IsNullOrWhiteSpace(request.Nickname) ? null : request.Nickname,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.CoreUsers.Add(user);
        dbContext.PlatformUserProfiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CacheIdentityAsync(profile, cancellationToken);
        return profile;
    }

    public async Task CacheUserIdentitiesAsync(CoreUser user, CancellationToken cancellationToken = default)
    {
        foreach (var profile in user.PlatformProfiles)
        {
            await identityCache.SetAsync(
                profile.Platform,
                profile.Uid,
                new CachedIdentity(user.Id, user.Privilege),
                cancellationToken);
        }
    }

    private Task CacheIdentityAsync(PlatformUserProfile profile, CancellationToken cancellationToken)
    {
        return identityCache.SetAsync(
            profile.Platform,
            profile.Uid,
            new CachedIdentity(profile.CoreUserId!.Value, profile.CoreUser!.Privilege),
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
