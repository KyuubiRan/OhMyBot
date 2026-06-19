using Grpc.Net.Client;
using OhMyBot.Contracts.Grpc;

namespace OhMyBot.TelegramGateway;

public interface ICommandRouterClient
{
    Task<CommandResponse> ExecuteCommandAsync(CommandRequest request, CancellationToken cancellationToken = default);

    Task<GetRoutesResponse> GetRoutesAsync(GetRoutesRequest request, CancellationToken cancellationToken = default);
}

public sealed class CommandRouterClientAdapter(CommandRouter.CommandRouterClient client) : ICommandRouterClient
{
    public async Task<CommandResponse> ExecuteCommandAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        return await client.ExecuteCommandAsync(request, cancellationToken: cancellationToken);
    }

    public async Task<GetRoutesResponse> GetRoutesAsync(GetRoutesRequest request, CancellationToken cancellationToken = default)
    {
        return await client.GetRoutesAsync(request, cancellationToken: cancellationToken);
    }
}

public static class CommandRouterClientFactory
{
    public static ICommandRouterClient Create(string coreAddress)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false
        };
        var channel = GrpcChannel.ForAddress(coreAddress, new GrpcChannelOptions
        {
            HttpHandler = handler
        });
        return new CommandRouterClientAdapter(new CommandRouter.CommandRouterClient(channel));
    }
}
