using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.UserProfiles;

public interface IUserProfileCache
{
    Task<UserProfileCacheEntry?> GetAsync(
        BotPlatform platform,
        string uid,
        CancellationToken cancellationToken = default);

    Task SetAsync(
        UserProfileCacheEntry entry,
        CancellationToken cancellationToken = default);
}
