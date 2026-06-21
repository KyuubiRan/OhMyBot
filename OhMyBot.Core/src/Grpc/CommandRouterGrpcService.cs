using Grpc.Core;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Callbacks;
using OhMyBot.Core.Commands;
using OhMyBot.Core.UserProfiles;

namespace OhMyBot.Core.Grpc;

public sealed class CommandRouterGrpcService(
    CommandExecutionService commandExecutionService,
    CallbackExecutionService callbackExecutionService,
    PlatformUserProfileService userProfileService) : CommandRouter.CommandRouterBase
{
    public override Task<CommandResponse> ExecuteCommand(CommandRequest request, ServerCallContext context)
    {
        return commandExecutionService.ExecuteAsync(request, context.CancellationToken);
    }

    public override Task<GetRoutesResponse> GetRoutes(GetRoutesRequest request, ServerCallContext context)
    {
        return commandExecutionService.GetRoutesAsync(request, context.CancellationToken);
    }

    public override Task<CommandResponse> ExecuteCallback(CallbackRequest request, ServerCallContext context)
    {
        return callbackExecutionService.ExecuteAsync(request, context.CancellationToken);
    }

    public override async Task<UserProfileResponse> RecordUserProfile(UserProfileRequest request, ServerCallContext context)
    {
        await userProfileService.RecordAsync(request, context.CancellationToken);
        return new UserProfileResponse { Recorded = true };
    }
}
