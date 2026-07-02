using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Infrastructure.Identity;

public sealed record ResolvedIdentity(
    long CoreUserId,
    UserPrivilege Privilege,
    BotPlatform Platform,
    string PlatformUserId);
