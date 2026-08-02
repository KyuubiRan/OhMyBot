using Grpc.Core;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts;
using OhMyBot.Core.Infrastructure.Grpc;

namespace OhMyBot.Tests;

/// <summary>
/// Core gRPC 同时承载命令路由与管理控制台（<c>OpenAdminConsole</c> 可执行提权命令），
/// 且默认是明文 HTTP/2。这里验证「没有正确令牌就一定进不来」这条边界。
/// </summary>
[TestClass]
public class GrpcAccessTokenTests
{
    private const string ValidToken = "correct-horse-battery-staple";

    [TestMethod]
    public async Task RequestWithMatchingTokenIsAllowed()
    {
        var interceptor = CreateInterceptor(ValidToken);
        var context = CreateContext(ValidToken);

        var response = await interceptor.UnaryServerHandler<string, string>(
            "request",
            context,
            (_, _) => Task.FromResult("ok"));

        Assert.AreEqual("ok", response);
    }

    [TestMethod]
    public async Task RequestWithoutTokenIsUnauthenticated()
    {
        // 缺头是最常见的「网关没配令牌」形态，绝不能被当作匿名放行。
        var interceptor = CreateInterceptor(ValidToken);
        var context = CreateContext(null);

        var exception = await Assert.ThrowsExactlyAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                context,
                (_, _) => Task.FromResult("ok")));

        Assert.AreEqual(StatusCode.Unauthenticated, exception.StatusCode);
    }

    [TestMethod]
    public async Task RequestWithWrongTokenIsUnauthenticated()
    {
        var interceptor = CreateInterceptor(ValidToken);
        var context = CreateContext("wrong-token");

        var exception = await Assert.ThrowsExactlyAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                context,
                (_, _) => Task.FromResult("ok")));

        Assert.AreEqual(StatusCode.Unauthenticated, exception.StatusCode);
    }

    [TestMethod]
    public async Task AdminConsoleStreamRequiresToken()
    {
        // OpenAdminConsole 是 duplex streaming：unary 拦到了不代表它也拦得到，
        // 而它恰恰是权限最高的入口（可执行 user -sp owner）。
        var interceptor = CreateInterceptor(ValidToken);
        var context = CreateContext(null);
        var invoked = false;

        var exception = await Assert.ThrowsExactlyAsync<RpcException>(() =>
            interceptor.DuplexStreamingServerHandler<string, string>(
                new EmptyStreamReader(),
                new NullStreamWriter(),
                context,
                (_, _, _) =>
                {
                    invoked = true;
                    return Task.CompletedTask;
                }));

        Assert.AreEqual(StatusCode.Unauthenticated, exception.StatusCode);
        Assert.IsFalse(invoked, "未通过鉴权时不应进入管理控制台处理逻辑。");
    }

    private static AccessTokenInterceptor CreateInterceptor(string token)
    {
        return new AccessTokenInterceptor(Options.Create(new GrpcAccessOptions { AccessToken = token }));
    }

    private static ServerCallContext CreateContext(string? token)
    {
        var headers = new Metadata();
        if (token is not null)
        {
            headers.Add(GrpcAccessCredentials.HeaderName, token);
        }

        return new StubServerCallContext(headers);
    }

    private sealed class EmptyStreamReader : IAsyncStreamReader<string>
    {
        public string Current => string.Empty;

        public Task<bool> MoveNext(CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class NullStreamWriter : IServerStreamWriter<string>
    {
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(string message) => Task.CompletedTask;
    }

    /// <summary>
    /// 最小 <see cref="ServerCallContext"/> 替身：拦截器只读 RequestHeaders，
    /// 其余成员仅为满足抽象基类，不参与断言。
    /// </summary>
    private sealed class StubServerCallContext(Metadata requestHeaders) : ServerCallContext
    {
        protected override string MethodCore => "/test/Method";

        protected override string HostCore => "localhost";

        protected override string PeerCore => "ipv4:127.0.0.1:5100";

        protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(1);

        protected override Metadata RequestHeadersCore { get; } = requestHeaders;

        protected override CancellationToken CancellationTokenCore => CancellationToken.None;

        protected override Metadata ResponseTrailersCore { get; } = [];

        protected override Status StatusCore { get; set; }

        protected override WriteOptions? WriteOptionsCore { get; set; }

        protected override AuthContext AuthContextCore { get; } = new(null, new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
            => throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
