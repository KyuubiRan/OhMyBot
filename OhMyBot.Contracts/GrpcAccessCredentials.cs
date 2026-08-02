using Grpc.Core;

namespace OhMyBot.Contracts;

/// <summary>
/// Core gRPC 共享令牌的客户端凭证。Core 侧由 AccessTokenInterceptor 校验同名 metadata 头。
/// 两个网关与远程控制台共用这里，避免头名称在三处各写一份而漂移。
/// </summary>
public static class GrpcAccessCredentials
{
    public const string HeaderName = "x-ohmybot-token";

    /// <summary>
    /// 构造附带令牌的 <see cref="ChannelCredentials"/>。令牌为空时仍然构造（附空值），
    /// 让 Core 统一以 Unauthenticated 拒绝，而不是客户端静默地不带头发出请求。
    /// </summary>
    public static ChannelCredentials Create(string? accessToken)
    {
        var token = accessToken ?? string.Empty;
        return ChannelCredentials.Create(
            ChannelCredentials.Insecure,
            CallCredentials.FromInterceptor((_, metadata) =>
            {
                metadata.Add(HeaderName, token);
                return Task.CompletedTask;
            }));
    }
}
