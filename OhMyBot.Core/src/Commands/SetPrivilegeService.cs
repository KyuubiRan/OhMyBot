using Microsoft.EntityFrameworkCore;
using OhMyBot.Contracts;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Data;
using OhMyBot.Core.Data.Entities;
using OhMyBot.Core.Identity;

namespace OhMyBot.Core.Commands;

public sealed class SetPrivilegeService(
    OhMyBotV2DbContext dbContext,
    IIdentityCache identityCache,
    TimeProvider timeProvider)
{
    public async Task<SetPrivilegeTarget?> FindTargetAsync(
        BotPlatform platform,
        string requestedUser,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestedUser))
        {
            return null;
        }

        var normalized = requestedUser.Trim();
        var username = normalized.StartsWith('@')
            ? normalized.Substring(1)
            : normalized;
        var normalizedUsername = username.ToLowerInvariant();
        var searchByUsernameOnly = normalized.StartsWith("@", StringComparison.Ordinal);

        var profile = await dbContext.PlatformUserProfiles
            .Include(item => item.CoreUser!)
            .ThenInclude(user => user.PlatformProfiles)
            .FirstOrDefaultAsync(
                item => item.Platform == platform
                    && (searchByUsernameOnly
                        ? item.Username != null && item.Username.ToLower() == normalizedUsername
                        : item.Uid == normalized
                            || item.Username != null && item.Username.ToLower() == normalizedUsername),
                cancellationToken);

        return profile is null ? null : ToTarget(profile);
    }

    public async Task<SetPrivilegeResult> SetAsync(
        long operatorCoreUserId,
        UserPrivilege operatorPrivilege,
        BotPlatform platform,
        string targetUid,
        UserPrivilege newPrivilege,
        CancellationToken cancellationToken)
    {
        var target = await dbContext.PlatformUserProfiles
            .Include(item => item.CoreUser!)
            .ThenInclude(user => user.PlatformProfiles)
            .FirstOrDefaultAsync(
                item => item.Platform == platform && item.Uid == targetUid,
                cancellationToken);

        if (target is null)
        {
            return SetPrivilegeResult.Missing();
        }

        var currentPrivilege = target.CoreUser?.Privilege ?? UserPrivilege.User;
        if (!CanSet(operatorPrivilege, newPrivilege) || (target.CoreUserId is not null && !CanOperateTarget(operatorCoreUserId, operatorPrivilege, target.CoreUserId.Value, currentPrivilege)))
        {
            return SetPrivilegeResult.Rejected(ToTarget(target), currentPrivilege, newPrivilege);
        }

        var now = timeProvider.GetUtcNow();
        if (target.CoreUser is null)
        {
            target.CoreUser = new CoreUser
            {
                Privilege = newPrivilege,
                CreatedAt = now,
                UpdatedAt = now
            };
            target.UpdatedAt = now;
            dbContext.CoreUsers.Add(target.CoreUser);
        }
        else
        {
            target.CoreUser.Privilege = newPrivilege;
            target.CoreUser.UpdatedAt = now;
            target.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await CacheUserProfilesAsync(target.CoreUser!, cancellationToken);
        return SetPrivilegeResult.Updated(ToTarget(target), currentPrivilege, newPrivilege);
    }

    public static IReadOnlyList<UserPrivilege> GetAllowedTargetPrivileges(UserPrivilege operatorPrivilege)
    {
        return operatorPrivilege switch
        {
            UserPrivilege.Owner => [UserPrivilege.User, UserPrivilege.VerifiedUser, UserPrivilege.Admin],
            UserPrivilege.Admin => [UserPrivilege.User, UserPrivilege.VerifiedUser],
            _ => []
        };
    }

    public static bool CanSet(UserPrivilege operatorPrivilege, UserPrivilege newPrivilege)
    {
        return GetAllowedTargetPrivileges(operatorPrivilege).Contains(newPrivilege);
    }

    public static bool CanOperateTarget(long operatorCoreUserId, UserPrivilege operatorPrivilege, long targetCoreUserId, UserPrivilege targetPrivilege)
    {
        return operatorCoreUserId != targetCoreUserId && (int)targetPrivilege < (int)operatorPrivilege;
    }

    public static string FormatPrivilege(UserPrivilege privilege)
    {
        return UserPrivilegeNames.Format(privilege);
    }

    private async Task CacheUserProfilesAsync(CoreUser user, CancellationToken cancellationToken)
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

    private static SetPrivilegeTarget ToTarget(PlatformUserProfile profile)
    {
        return new SetPrivilegeTarget(
            profile.Platform,
            profile.Uid,
            FormatDisplayName(profile),
            profile.CoreUserId,
            profile.CoreUser?.Privilege ?? UserPrivilege.User);
    }

    private static string FormatDisplayName(PlatformUserProfile profile)
    {
        var name = string.Join(' ', new[] { profile.LastName, profile.FirstName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return FirstNonEmpty(profile.Nickname, name, profile.Username is null ? null : "@" + profile.Username, profile.Uid);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }
}

public sealed record SetPrivilegeTarget(
    BotPlatform Platform,
    string Uid,
    string DisplayName,
    long? CoreUserId,
    UserPrivilege CurrentPrivilege);

public sealed record SetPrivilegeResult(
    bool Success,
    bool IsNotFound,
    bool IsForbidden,
    SetPrivilegeTarget? Target,
    UserPrivilege Before,
    UserPrivilege After)
{
    public static SetPrivilegeResult Missing()
    {
        return new SetPrivilegeResult(false, true, false, null, UserPrivilege.User, UserPrivilege.User);
    }

    public static SetPrivilegeResult Rejected(SetPrivilegeTarget target, UserPrivilege before, UserPrivilege after)
    {
        return new SetPrivilegeResult(false, false, true, target, before, after);
    }

    public static SetPrivilegeResult Updated(SetPrivilegeTarget target, UserPrivilege before, UserPrivilege after)
    {
        return new SetPrivilegeResult(true, false, false, target, before, after);
    }
}
