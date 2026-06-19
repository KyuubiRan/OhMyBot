using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Data;
using OhMyBot.Core.Data.Entities;
using OhMyBot.Core.Linking;

namespace OhMyBot.Core.Commands;

public sealed class LinkCommand(
    IServiceScopeFactory scopeFactory,
    IOptions<LinkTokenOptions> linkTokenOptions,
    TimeProvider timeProvider) : ICoreCommand
{
    private readonly LinkTokenOptions _linkTokenOptions = linkTokenOptions.Value;

    public string Name => "link";

    public string Description => "Link identities across platforms.";

    public string Usage => "/link [token]";

    public IReadOnlyList<string> Aliases => [];

    public UserPrivilege RequiredPrivilege => UserPrivilege.User;

    public SupportedPlatforms SupportPlatforms => SupportedPlatforms.All;

    public bool Enabled => true;

    public async Task<CommandResponse> ExecuteAsync(CommandContext context)
    {
        var request = context.Request;
        var currentIdentity = context.Identity;
        var cancellationToken = context.CancellationToken;
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OhMyBotV2DbContext>();
        var identityService = scope.ServiceProvider.GetRequiredService<CoreIdentityService>();
        var linkTokenStore = scope.ServiceProvider.GetRequiredService<ILinkTokenStore>();

        if (request.Args.Count == 0)
        {
            var token = GenerateToken();
            var payload = new LinkTokenPayload(
                currentIdentity.CoreUserId,
                currentIdentity.Platform,
                currentIdentity.PlatformUserId,
                timeProvider.GetUtcNow());

            await linkTokenStore.SetAsync(token, payload, _linkTokenOptions.TokenTtl, cancellationToken);
            var response = CommandResponses.Ok(CommandResponseDataKind.LinkToken, context);
            response.LinkToken = new LinkTokenData
            {
                Token = token,
                TtlSeconds = (int)_linkTokenOptions.TokenTtl.TotalSeconds
            };
            return response;
        }

        var incomingToken = request.Args[0].Trim();
        if (string.IsNullOrWhiteSpace(incomingToken))
        {
            return CommandResponses.Error("LinkTokenInvalid", "Link token is empty.", context);
        }

        var tokenPayload = await linkTokenStore.GetAsync(incomingToken, cancellationToken);
        if (tokenPayload is null)
        {
            return CommandResponses.Error("LinkTokenInvalid", "Link token does not exist, expired, or was already consumed.", context);
        }

        var targetUser = await dbContext.CoreUsers
            .Include(user => user.Identities)
            .FirstOrDefaultAsync(user => user.Id == tokenPayload.OwnerCoreUserId, cancellationToken);

        if (targetUser is null)
        {
            await linkTokenStore.RemoveAsync(incomingToken, cancellationToken);
            return CommandResponses.Error("LinkTargetMissing", "The link token owner no longer exists.", context);
        }

        var sourceUser = await dbContext.CoreUsers
            .Include(user => user.Identities)
            .FirstAsync(user => user.Id == currentIdentity.CoreUserId, cancellationToken);

        if (sourceUser.Id == targetUser.Id)
        {
            await linkTokenStore.RemoveAsync(incomingToken, cancellationToken);
            var response = CommandResponses.Ok(CommandResponseDataKind.LinkResult, context);
            response.LinkResult = new LinkResultData
            {
                Status = "already_linked",
                CoreUserId = sourceUser.Id
            };
            return response;
        }

        var (retainedUser, mergedUser) = SelectMergeDirection(sourceUser, targetUser);
        await MergeUsersAsync(dbContext, mergedUser, retainedUser, cancellationToken);
        await identityService.CacheUserIdentitiesAsync(retainedUser, cancellationToken);
        await linkTokenStore.RemoveAsync(incomingToken, cancellationToken);
        var linkResponse = CommandResponses.Ok(CommandResponseDataKind.LinkResult, context);
        linkResponse.LinkResult = new LinkResultData
        {
            Status = "linked",
            CoreUserId = retainedUser.Id
        };
        return linkResponse;
    }

    private static (CoreUser RetainedUser, CoreUser MergedUser) SelectMergeDirection(CoreUser firstUser, CoreUser secondUser)
    {
        return firstUser.Id.CompareTo(secondUser.Id) <= 0
            ? (firstUser, secondUser)
            : (secondUser, firstUser);
    }

    private async Task MergeUsersAsync(
        OhMyBotV2DbContext dbContext,
        CoreUser sourceUser,
        CoreUser targetUser,
        CancellationToken cancellationToken)
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
