using Grpc.Core;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commands;

namespace OhMyBot.Core.Grpc;

public sealed class CommandRouterGrpcService(CommandExecutionService commandExecutionService) : CommandRouter.CommandRouterBase
{
    public override Task<CommandResponse> ExecuteCommand(CommandRequest request, ServerCallContext context)
    {
        return commandExecutionService.ExecuteAsync(request, context.CancellationToken);
    }

    public override Task<GetRoutesResponse> GetRoutes(GetRoutesRequest request, ServerCallContext context)
    {
        return commandExecutionService.GetRoutesAsync(request, context.CancellationToken);
    }
}
