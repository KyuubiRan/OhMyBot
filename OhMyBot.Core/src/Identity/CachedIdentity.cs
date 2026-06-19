using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Identity;

public sealed record CachedIdentity(long CoreUserId, UserPrivilege Privilege);
