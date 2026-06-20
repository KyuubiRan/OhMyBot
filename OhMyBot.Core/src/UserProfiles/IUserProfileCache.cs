using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.UserProfiles;

public interface IUserProfileCache
{
    Task<UserProfileUpdate?> GetAsync(
        BotPlatform platform,
        string uid,
        CancellationToken cancellationToken = default);

    Task SetAsync(
        UserProfileUpdate profile,
        CancellationToken cancellationToken = default);
}
