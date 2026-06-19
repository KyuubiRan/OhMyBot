using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Data;
using OhMyBot.Core.Data.Entities;
using OhMyBot.Core.Identity;
using OhMyBot.Core.Linking;
using OhMyBot.Core.Routing;

namespace OhMyBot.Core.Commands;

public sealed class CommandExecutionService(
    OhMyBotV2DbContext dbContext,
    CoreIdentityService identityService,
    CommandRegistry commandRegistry,
    RouteStore routeStore,
    ILinkTokenStore linkTokenStore,
    IOptions<LinkTokenOptions> linkTokenOptions,
    TimeProvider timeProvider)
{
    private readonly LinkTokenOptions _linkTokenOptions = linkTokenOptions.Value;

    public async Task<CommandResponse> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var identity = await identityService.ResolveIdentityAsync(request, cancellationToken);

        if (!routeStore.TryGet(request.Command, out var route))
        {
            return CommandResponses.Error("RouteNotFound", "Unknown route.", request.MessageId);
        }

        if (!route.Enabled)
        {
            return CommandResponses.Error("RouteDisabled", "This route is disabled.", request.MessageId);
        }

        if (!route.TargetExists || !commandRegistry.TryGet(route.CoreCommand, out var command) || !command.Enabled)
        {
            return CommandResponses.Error("RouteTargetMissing", "The route target command is not available.", request.MessageId);
        }

        var platformFlag = CommandRegistry.ToSupportedPlatform(request.Platform);
        if (platformFlag is SupportedPlatforms.None || !route.SupportPlatforms.HasFlag(platformFlag) || !command.SupportPlatforms.HasFlag(platformFlag))
        {
            return CommandResponses.Error("UnsupportedPlatform", "This command is not available on this platform.", request.MessageId);
        }

        if ((int)identity.Privilege < (int)route.EffectiveRequiredPrivilege)
        {
            return CommandResponses.Error("PrivilegeDenied", "Insufficient privilege.", request.MessageId);
        }

        if (command.Name == "ping")
        {
            return CommandResponses.Text($"Pong\nCore: {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms", request.MessageId);
        }

        if (command.Name == "link")
        {
            return await ExecuteLinkAsync(request, identity, cancellationToken);
        }

        return await command.ExecuteAsync(new CommandContext(request, identity, cancellationToken));
    }

    public Task<GetRoutesResponse> GetRoutesAsync(GetRoutesRequest request, CancellationToken cancellationToken = default)
    {
        var currentVersion = routeStore.Version;
        var response = new GetRoutesResponse
        {
            Version = currentVersion,
            NotModified = request.CurrentVersion > 0 && request.CurrentVersion == currentVersion
        };

        if (response.NotModified)
        {
            return Task.FromResult(response);
        }

        response.Routes.AddRange(routeStore.GetRoutes(request.Platform).Select(route => route.ToDescriptor()));
        return Task.FromResult(response);
    }

    public static IEnumerable<CommandRegistration> CreateBuiltInCommands()
    {
        yield return new CommandRegistration(
            "ping",
            "Check Core connectivity.",
            "/ping",
            UserPrivilege.User,
            SupportedPlatforms.All,
            _ => Task.FromResult(CommandResponses.Text("Pong")));

        yield return new CommandRegistration(
            "link",
            "Link identities across platforms.",
            "/link [token]",
            UserPrivilege.User,
            SupportedPlatforms.All,
            _ => Task.FromResult(CommandResponses.Error("InternalError", "Link command handler is not wired.")));
    }

    private async Task<CommandResponse> ExecuteLinkAsync(
        CommandRequest request,
        ResolvedIdentity currentIdentity,
        CancellationToken cancellationToken)
    {
        if (request.Args.Count == 0)
        {
            var token = GenerateToken();
            var payload = new LinkTokenPayload(
                currentIdentity.CoreUserId,
                currentIdentity.Platform,
                currentIdentity.PlatformUserId,
                timeProvider.GetUtcNow());

            await linkTokenStore.SetAsync(token, payload, _linkTokenOptions.TokenTtl, cancellationToken);
            return CommandResponses.Text($"Link token: {token}\nValid for 5 minutes.", request.MessageId);
        }

        var incomingToken = request.Args[0].Trim();
        if (string.IsNullOrWhiteSpace(incomingToken))
        {
            return CommandResponses.Error("LinkTokenInvalid", "Link token is empty.", request.MessageId);
        }

        var tokenPayload = await linkTokenStore.GetAsync(incomingToken, cancellationToken);
        if (tokenPayload is null)
        {
            return CommandResponses.Error("LinkTokenInvalid", "Link token does not exist, expired, or was already consumed.", request.MessageId);
        }

        var targetUser = await dbContext.CoreUsers
            .Include(user => user.Identities)
            .FirstOrDefaultAsync(user => user.Id == tokenPayload.OwnerCoreUserId, cancellationToken);

        if (targetUser is null)
        {
            await linkTokenStore.RemoveAsync(incomingToken, cancellationToken);
            return CommandResponses.Error("LinkTargetMissing", "The link token owner no longer exists.", request.MessageId);
        }

        var sourceUser = await dbContext.CoreUsers
            .Include(user => user.Identities)
            .FirstAsync(user => user.Id == currentIdentity.CoreUserId, cancellationToken);

        if (sourceUser.Id == targetUser.Id)
        {
            await linkTokenStore.RemoveAsync(incomingToken, cancellationToken);
            return CommandResponses.Text("Already linked.", request.MessageId);
        }

        var (retainedUser, mergedUser) = SelectMergeDirection(sourceUser, targetUser);
        await MergeUsersAsync(mergedUser, retainedUser, cancellationToken);
        await identityService.CacheUserIdentitiesAsync(retainedUser, cancellationToken);
        await linkTokenStore.RemoveAsync(incomingToken, cancellationToken);
        return CommandResponses.Text("Link succeeded.", request.MessageId);
    }

    private static (CoreUser RetainedUser, CoreUser MergedUser) SelectMergeDirection(CoreUser firstUser, CoreUser secondUser)
    {
        return firstUser.Id.CompareTo(secondUser.Id) <= 0
            ? (firstUser, secondUser)
            : (secondUser, firstUser);
    }

    private async Task MergeUsersAsync(CoreUser sourceUser, CoreUser targetUser, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        targetUser.Privilege = (UserPrivilege)Math.Max((int)targetUser.Privilege, (int)sourceUser.Privilege);
        targetUser.UpdatedAt = now;

        foreach (var identity in sourceUser.Identities.ToArray())
        {
            identity.CoreUserId = targetUser.Id;
            identity.CoreUser = targetUser;
            identity.UpdatedAt = now;
            targetUser.Identities.Add(identity);
        }

        dbContext.CoreUsers.Remove(sourceUser);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
