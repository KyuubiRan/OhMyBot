using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Identity;

public interface IIdentityCache
{
    Task<CachedIdentity?> GetAsync(BotPlatform platform, string platformUserId, CancellationToken cancellationToken = default);

    Task SetAsync(BotPlatform platform, string platformUserId, CachedIdentity identity, CancellationToken cancellationToken = default);

    Task RemoveAsync(BotPlatform platform, string platformUserId, CancellationToken cancellationToken = default);
}
