using Microsoft.EntityFrameworkCore;
using OhMyBot.Contracts;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Data;
using OhMyBot.Core.Data.Entities;
using OhMyBot.Core.Identity;

namespace OhMyBot.Core.Admin;

public sealed class UserAdminCommand(
    OhMyBotV2DbContext dbContext,
    IIdentityCache identityCache,
    TimeProvider timeProvider) : IAdminCommand
{
    private static readonly AdminCommandDefinition CommandDefinition = new(
        "user",
        "user (-id|--id=<core-user-id> | -p|--platform=<platform> -uid|--uid=<uid>) [-sp|--set-priv=<priv>] [-gp|--get-priv]",
        "Manage core/platform users.",
        [],
        [
            new AdminCommandOptionDefinition("-id, --id", "specify core user id"),
            new AdminCommandOptionDefinition("-p, --platform", "specify platform: telegram, qq"),
            new AdminCommandOptionDefinition("-uid, --uid", "specify platform user id, required with platform"),
            new AdminCommandOptionDefinition("-sp, --set-priv", $"set privileges: {UserPrivilegeNames.SupportedValues}"),
            new AdminCommandOptionDefinition("-gp, --get-priv", "get privileges")
        ],
        [
            "user -id 1",
            "user -p telegram -uid 123456",
            "user -p telegram -uid 123456 -sp owner"
        ]);

    private static readonly IReadOnlyDictionary<string, AdminCommandOptionSpec> OptionSpecs =
        new Dictionary<string, AdminCommandOptionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = new("id", IsFlag: false),
            ["p"] = new("platform", IsFlag: false),
            ["platform"] = new("platform", IsFlag: false),
            ["uid"] = new("uid", IsFlag: false),
            ["sp"] = new("set-priv", IsFlag: false),
            ["set-priv"] = new("set-priv", IsFlag: false),
            ["priv"] = new("set-priv", IsFlag: false),
            ["gp"] = new("get-priv", IsFlag: true),
            ["get-priv"] = new("get-priv", IsFlag: true)
        };

    public AdminCommandDefinition Definition => CommandDefinition;

    public async Task<AdminCommandResult> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        AdminCommandParseResult parsed;
        try
        {
            parsed = AdminCommandParser.ParseOptions(args, OptionSpecs);
        }
        catch (InvalidOperationException exception)
        {
            return UsageError(exception.Message);
        }

        var selector = ParseUserSelector(parsed.Options);
        if (!selector.Success)
        {
            return UsageError(selector.ErrorMessage);
        }

        if (parsed.Options.TryGetValue("set-priv", out var privilegeValue))
        {
            if (!TryParsePrivilege(privilegeValue, out var privilege))
            {
                return UsageError($"Invalid privilege. Supported values: {UserPrivilegeNames.SupportedValues}.");
            }

            return selector.CoreUserId.HasValue
                ? await SetUserPrivilegeByIdAsync(selector.CoreUserId.Value, privilege, cancellationToken)
                : await SetUserPrivilegeByPlatformAsync(selector.Platform!.Value, selector.PlatformUserId!, privilege, cancellationToken);
        }

        return selector.CoreUserId.HasValue
            ? await GetUserByIdAsync(selector.CoreUserId.Value, cancellationToken)
            : await GetUserByPlatformAsync(selector.Platform!.Value, selector.PlatformUserId!, cancellationToken);
    }

    private UserSelector ParseUserSelector(IReadOnlyDictionary<string, string> options)
    {
        var hasId = options.TryGetValue("id", out var idValue);
        var hasPlatform = options.TryGetValue("platform", out var platformValue);
        var hasUid = options.TryGetValue("uid", out var platformUserId);

        if (hasId && (hasPlatform || hasUid))
        {
            return UserSelector.Invalid("Options '-id' and '-p/-uid' are mutually exclusive.");
        }

        if (!hasId && !hasPlatform && !hasUid)
        {
            return UserSelector.Invalid("A user selector is required: use '-id' or '-p' with '-uid'.");
        }

        if (hasId)
        {
            return long.TryParse(idValue, out var coreUserId) && coreUserId > 0
                ? UserSelector.ById(coreUserId)
                : UserSelector.Invalid("Invalid core user id.");
        }

        if (!hasPlatform || !hasUid)
        {
            return UserSelector.Invalid("Options '-p' and '-uid' must be specified together.");
        }

        if (!TryParsePlatform(platformValue!, out var platform))
        {
            return UserSelector.Invalid("Invalid platform. Supported values: telegram, qq.");
        }

        return string.IsNullOrWhiteSpace(platformUserId)
            ? UserSelector.Invalid("Platform user id cannot be empty.")
            : UserSelector.ByPlatform(platform, platformUserId.Trim());
    }

    private async Task<AdminCommandResult> GetUserByIdAsync(long coreUserId, CancellationToken cancellationToken)
    {
        var user = await dbContext.CoreUsers
            .AsNoTracking()
            .Include(item => item.Identities)
            .FirstOrDefaultAsync(item => item.Id == coreUserId, cancellationToken);

        if (user is null)
        {
            return AdminCommandResult.Error($"Core user not found: id={coreUserId}.");
        }

        return AdminCommandResult.Ok(FormatUser(user));
    }

    private async Task<AdminCommandResult> GetUserByPlatformAsync(
        BotPlatform platform,
        string platformUserId,
        CancellationToken cancellationToken)
    {
        var identity = await dbContext.PlatformIdentities
            .Include(item => item.CoreUser)
            .ThenInclude(user => user.Identities)
            .FirstOrDefaultAsync(
                item => item.Platform == platform && item.PlatformUserId == platformUserId,
                cancellationToken);

        if (identity is null)
        {
            return AdminCommandResult.Error($"User identity not found: platform={FormatPlatform(platform)}, uid={platformUserId}.");
        }

        return AdminCommandResult.Ok(FormatUser(identity.CoreUser));
    }

    private async Task<AdminCommandResult> SetUserPrivilegeByIdAsync(
        long coreUserId,
        UserPrivilege privilege,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.CoreUsers
            .Include(item => item.Identities)
            .FirstOrDefaultAsync(item => item.Id == coreUserId, cancellationToken);

        if (user is null)
        {
            return AdminCommandResult.Error($"Core user not found: id={coreUserId}.");
        }

        user.Privilege = privilege;
        user.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        await CacheUserIdentitiesAsync(user, cancellationToken);

        return AdminCommandResult.Ok($"User privilege set: id={user.Id}, privilege={FormatPrivilege(privilege)}.");
    }

    private async Task<AdminCommandResult> SetUserPrivilegeByPlatformAsync(
        BotPlatform platform,
        string platformUserId,
        UserPrivilege privilege,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var identity = await dbContext.PlatformIdentities
            .Include(item => item.CoreUser)
            .ThenInclude(user => user.Identities)
            .FirstOrDefaultAsync(
                item => item.Platform == platform && item.PlatformUserId == platformUserId,
                cancellationToken);

        if (identity is null)
        {
            var user = new CoreUser
            {
                Privilege = privilege,
                CreatedAt = now,
                UpdatedAt = now
            };

            identity = new PlatformIdentity
            {
                CoreUser = user,
                Platform = platform,
                PlatformUserId = platformUserId,
                CreatedAt = now,
                UpdatedAt = now
            };

            dbContext.CoreUsers.Add(user);
            dbContext.PlatformIdentities.Add(identity);
        }
        else
        {
            identity.CoreUser.Privilege = privilege;
            identity.CoreUser.UpdatedAt = now;
            identity.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await CacheUserIdentitiesAsync(identity.CoreUser, cancellationToken);

        return AdminCommandResult.Ok(
            $"User privilege set: id={identity.CoreUserId}, platform={FormatPlatform(platform)}, uid={platformUserId}, privilege={FormatPrivilege(privilege)}.");
    }

    private async Task CacheUserIdentitiesAsync(CoreUser user, CancellationToken cancellationToken)
    {
        foreach (var identity in user.Identities)
        {
            await identityCache.SetAsync(
                identity.Platform,
                identity.PlatformUserId,
                new CachedIdentity(user.Id, user.Privilege),
                cancellationToken);
        }
    }

    private AdminCommandResult UsageError(string message)
    {
        return AdminCommandResult.Error($"{message}{Environment.NewLine}{Environment.NewLine}usage: {Definition.Usage}");
    }

    private static bool TryParsePlatform(string value, out BotPlatform platform)
    {
        platform = value.Trim().ToLowerInvariant() switch
        {
            "telegram" or "tg" => BotPlatform.Telegram,
            "qq" => BotPlatform.Qq,
            _ => BotPlatform.Unspecified
        };

        return platform is not BotPlatform.Unspecified;
    }

    private static bool TryParsePrivilege(string value, out UserPrivilege privilege)
    {
        return UserPrivilegeNames.TryParse(value, out privilege);
    }

    private static string FormatUser(CoreUser user)
    {
        var lines = new List<string>
        {
            $"CoreUserId: {user.Id}",
            $"Privilege: {FormatPrivilege(user.Privilege)}"
        };

        if (user.Identities.Count > 0)
        {
            lines.Add("Identities:");
            lines.AddRange(user.Identities
                .OrderBy(identity => identity.Platform)
                .ThenBy(identity => identity.PlatformUserId, StringComparer.Ordinal)
                .Select(identity => $"  {FormatPlatform(identity.Platform)}:{identity.PlatformUserId}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatPlatform(BotPlatform platform)
    {
        return platform switch
        {
            BotPlatform.Telegram => "telegram",
            BotPlatform.Qq => "qq",
            _ => "unspecified"
        };
    }

    private static string FormatPrivilege(UserPrivilege privilege)
    {
        return UserPrivilegeNames.Format(privilege);
    }

    private sealed record UserSelector(
        bool Success,
        long? CoreUserId,
        BotPlatform? Platform,
        string? PlatformUserId,
        string ErrorMessage)
    {
        public static UserSelector ById(long coreUserId)
        {
            return new UserSelector(true, coreUserId, null, null, string.Empty);
        }

        public static UserSelector ByPlatform(BotPlatform platform, string platformUserId)
        {
            return new UserSelector(true, null, platform, platformUserId, string.Empty);
        }

        public static UserSelector Invalid(string errorMessage)
        {
            return new UserSelector(false, null, null, null, errorMessage);
        }
    }
}
