using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Data;
using OhMyBot.Core.Data.Entities;

namespace OhMyBot.Core.Commands;

public sealed class InfoCommand(IServiceScopeFactory scopeFactory) : ICoreCommand
{
    public string Name => "info";

    public string Description => "Show user information.";

    public string Usage => "/info [uid]";

    public IReadOnlyList<string> Aliases => [];

    public UserPrivilege RequiredPrivilege => UserPrivilege.User;

    public SupportedPlatforms SupportPlatforms => SupportedPlatforms.All;

    public bool Enabled => true;

    public async Task<CommandResponse> ExecuteAsync(CommandContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OhMyBotV2DbContext>();
        var callerIsAdmin = (int)context.Identity.Privilege >= (int)UserPrivilege.Admin;
        var requestedPlatformUserId = context.Request.Args.Count > 0
            ? context.Request.Args[0].Trim()
            : string.Empty;

        CoreUser? targetUser;
        if (callerIsAdmin && !string.IsNullOrWhiteSpace(requestedPlatformUserId))
        {
            var targetIdentity = await dbContext.PlatformIdentities
                .AsNoTracking()
                .Include(identity => identity.CoreUser)
                .ThenInclude(user => user.Identities)
                .FirstOrDefaultAsync(
                    identity => identity.Platform == context.Request.Platform
                        && identity.PlatformUserId == requestedPlatformUserId,
                    context.CancellationToken);

            if (targetIdentity is null)
            {
                return CommandResponses.Error(
                    "UserNotFound",
                    $"User identity not found: uid={requestedPlatformUserId}.",
                    context);
            }

            targetUser = targetIdentity.CoreUser;
        }
        else
        {
            targetUser = await dbContext.CoreUsers
                .AsNoTracking()
                .Include(user => user.Identities)
                .FirstOrDefaultAsync(user => user.Id == context.Identity.CoreUserId, context.CancellationToken);
        }

        if (targetUser is null)
        {
            return CommandResponses.Error("UserNotFound", "Current user was not found.", context);
        }

        var data = new UserInfoData
        {
            Self = targetUser.Id == context.Identity.CoreUserId,
            Privilege = targetUser.Privilege
        };

        data.Identities.AddRange(targetUser.Identities
                .OrderBy(identity => identity.Platform)
                .ThenBy(identity => identity.PlatformUserId, StringComparer.Ordinal)
                .Select(ToIdentityData));

        if (callerIsAdmin)
        {
            data.CoreUserId = targetUser.Id;
        }

        var response = CommandResponses.Ok(CommandResponseDataKind.UserInfo, context);
        response.UserInfo = data;
        return response;
    }

    private static PlatformIdentityData ToIdentityData(PlatformIdentity identity)
    {
        return new PlatformIdentityData
        {
            Platform = identity.Platform,
            Uid = identity.PlatformUserId,
            DisplayName = identity.DisplayName ?? string.Empty,
            Username = identity.Username ?? string.Empty
        };
    }
}
