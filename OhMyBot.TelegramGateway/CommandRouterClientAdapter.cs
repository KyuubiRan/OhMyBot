using Grpc.Net.Client;
using OhMyBot.Contracts;
using OhMyBot.Contracts.Grpc;

namespace OhMyBot.TelegramGateway;

public interface ICommandRouterClient
{
    Task<CommandResponse> ExecuteCommandAsync(CommandRequest request, CancellationToken cancellationToken = default);

    Task<CommandResponse> ExecuteCallbackAsync(CallbackRequest request, CancellationToken cancellationToken = default);

    Task<GetRoutesResponse> GetRoutesAsync(GetRoutesRequest request, CancellationToken cancellationToken = default);

    Task<UserProfileResponse> RecordUserProfileAsync(UserProfileRequest request, CancellationToken cancellationToken = default);
}

public sealed class CommandRouterClientAdapter(CommandRouter.CommandRouterClient client) : ICommandRouterClient
{
    public async Task<CommandResponse> ExecuteCommandAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        return await client.ExecuteCommandAsync(request, cancellationToken: cancellationToken);
    }

    public async Task<CommandResponse> ExecuteCallbackAsync(CallbackRequest request, CancellationToken cancellationToken = default)
    {
        return await client.ExecuteCallbackAsync(request, cancellationToken: cancellationToken);
    }

    public async Task<GetRoutesResponse> GetRoutesAsync(GetRoutesRequest request, CancellationToken cancellationToken = default)
    {
        return await client.GetRoutesAsync(request, cancellationToken: cancellationToken);
    }

    public async Task<UserProfileResponse> RecordUserProfileAsync(UserProfileRequest request, CancellationToken cancellationToken = default)
    {
        return await client.RecordUserProfileAsync(request, cancellationToken: cancellationToken);
    }
}

public static class CommandRouterClientFactory
{
    public static ICommandRouterClient Create(string coreAddress, string? accessToken)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false
        };
        var channel = GrpcChannel.ForAddress(coreAddress, new GrpcChannelOptions
        {
            HttpHandler = handler,
            Credentials = GrpcAccessCredentials.Create(accessToken),
            // Core 默认是明文 HTTP/2；不开这个开关 gRPC 会拒绝在非 TLS 通道上发送 CallCredentials。
            UnsafeUseInsecureChannelCallCredentials = true
        });
        return new CommandRouterClientAdapter(new CommandRouter.CommandRouterClient(channel));
    }
}
