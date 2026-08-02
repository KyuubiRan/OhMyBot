using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Options;

namespace OhMyBot.Core.Infrastructure.Grpc;

/// <summary>
/// 共享令牌鉴权。Core gRPC 承载命令路由与管理控制台（等价于提权入口），
/// 且默认是明文 HTTP/2，因此所有 RPC 都必须携带 <see cref="GrpcAccessOptions.HeaderName"/>。
/// 令牌在 Host 启动时校验非空，这里只做比对，不做「未配置则放行」的降级。
/// </summary>
public sealed class AccessTokenInterceptor(IOptions<GrpcAccessOptions> options) : Interceptor
{
    private readonly byte[] _expected = Encoding.UTF8.GetBytes(options.Value.AccessToken);

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        Authenticate(context);
        return continuation(request, context);
    }

    public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authenticate(context);
        return continuation(requestStream, context);
    }

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authenticate(context);
        return continuation(request, responseStream, context);
    }

    public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        Authenticate(context);
        return continuation(requestStream, responseStream, context);
    }

    private void Authenticate(ServerCallContext context)
    {
        var provided = context.RequestHeaders.GetValue(GrpcAccessOptions.HeaderName);
        if (provided is null || !IsExpected(provided))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid access token."));
        }
    }

    // 定长比较，避免以耗时差异逐字节猜测令牌。
    private bool IsExpected(string provided)
    {
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), _expected);
    }
}
