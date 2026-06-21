using Microsoft.EntityFrameworkCore;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Data;
using OhMyBot.Core.Data.Entities;

namespace OhMyBot.Core.UserProfiles;

public sealed class PlatformUserProfileService(
    OhMyBotV2DbContext dbContext,
    IUserProfileCache cache,
    TimeProvider timeProvider)
{
    public async Task RecordAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        await RecordAsync(UserProfileUpdate.FromRequest(request), cancellationToken);
    }

    public async Task RecordAsync(UserProfileRequest request, CancellationToken cancellationToken = default)
    {
        await RecordAsync(UserProfileUpdate.FromRequest(request), cancellationToken);
    }

    private async Task RecordAsync(UserProfileUpdate incoming, CancellationToken cancellationToken)
    {
        if (incoming.Platform is BotPlatform.Unspecified || string.IsNullOrWhiteSpace(incoming.Uid))
        {
            return;
        }

        var cached = await cache.GetAsync(incoming.Platform, incoming.Uid, cancellationToken);
        if (cached is { Persisted: true } && cached.Profile.HasSameProfile(incoming))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var profile = await dbContext.PlatformUserProfiles.FirstOrDefaultAsync(
            item => item.Platform == incoming.Platform && item.Uid == incoming.Uid,
            cancellationToken);

        if (profile is null)
        {
            profile = new PlatformUserProfile
            {
                Platform = incoming.Platform,
                Uid = incoming.Uid,
                Username = incoming.Username,
                FirstName = incoming.FirstName,
                LastName = incoming.LastName,
                Nickname = incoming.Nickname,
                CreatedAt = now,
                UpdatedAt = now
            };

            dbContext.PlatformUserProfiles.Add(profile);
            await dbContext.SaveChangesAsync(cancellationToken);
            await cache.SetAsync(UserProfileCacheEntry.PersistedProfile(incoming), cancellationToken);
            return;
        }

        if (!HasChanged(profile, incoming))
        {
            await cache.SetAsync(UserProfileCacheEntry.PersistedProfile(ToUpdate(profile)), cancellationToken);
            return;
        }

        profile.Username = incoming.Username;
        profile.FirstName = incoming.FirstName;
        profile.LastName = incoming.LastName;
        profile.Nickname = incoming.Nickname;
        profile.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        await cache.SetAsync(UserProfileCacheEntry.PersistedProfile(incoming), cancellationToken);
    }

    private static bool HasChanged(PlatformUserProfile profile, UserProfileUpdate incoming)
    {
        return !string.Equals(profile.Username, incoming.Username, StringComparison.Ordinal)
            || !string.Equals(profile.FirstName, incoming.FirstName, StringComparison.Ordinal)
            || !string.Equals(profile.LastName, incoming.LastName, StringComparison.Ordinal)
            || !string.Equals(profile.Nickname, incoming.Nickname, StringComparison.Ordinal);
    }

    private static UserProfileUpdate ToUpdate(PlatformUserProfile profile)
    {
        return new UserProfileUpdate(
            profile.Platform,
            profile.Uid,
            profile.Username,
            profile.FirstName,
            profile.LastName,
            profile.Nickname);
    }
}
