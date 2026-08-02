using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Infrastructure.Identity;

public sealed record CachedIdentity(long CoreUserId, UserPrivilege Privilege);
