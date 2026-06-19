using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Linking;

public sealed record LinkTokenPayload(
    long OwnerCoreUserId,
    BotPlatform CreatedFromPlatform,
    string CreatedFromPlatformUserId,
    DateTimeOffset CreatedAt);
