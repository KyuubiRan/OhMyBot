using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Routing;

namespace OhMyBot.Core.Commands;

public sealed class CommandExecutionService(
    CoreIdentityService identityService,
    CommandRegistry commandRegistry,
    RouteStore routeStore,
    TimeProvider timeProvider)
{
    public async Task<CommandResponse> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        var started = timeProvider.GetTimestamp();
        var identity = await identityService.ResolveIdentityAsync(request, cancellationToken);

        if (!routeStore.TryGet(request.Command, out var route))
        {
            return CommandResponses.Error("RouteNotFound", "Unknown route.", identity, request.MessageId);
        }

        if (!route.Enabled)
        {
            return CommandResponses.Error("RouteDisabled", "This route is disabled.", identity, request.MessageId);
        }

        if (!route.TargetExists || !commandRegistry.TryGet(route.CoreCommand, out var command) || !command.Enabled)
        {
            return CommandResponses.Error("RouteTargetMissing", "The route target command is not available.", identity, request.MessageId);
        }

        var platformFlag = CommandRegistry.ToSupportedPlatform(request.Platform);
        if (platformFlag is SupportedPlatforms.None || !route.SupportPlatforms.HasFlag(platformFlag) || !command.SupportPlatforms.HasFlag(platformFlag))
        {
            return CommandResponses.Error("UnsupportedPlatform", "This command is not available on this platform.", identity, request.MessageId);
        }

        if ((int)identity.Privilege < (int)route.EffectiveRequiredPrivilege)
        {
            return CommandResponses.Error("PrivilegeDenied", "Insufficient privilege.", identity, request.MessageId);
        }

        return await command.ExecuteAsync(new CommandContext(request, identity, started, cancellationToken));
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

}
