using OhMyBot.Contracts;

namespace OhMyBot.Core.Infrastructure.Grpc;

public sealed class GrpcAccessOptions
{
    /// <summary>Core gRPC 的共享访问令牌；网关与远程控制台必须携带同一值。</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>携带令牌的 metadata 头名称，与客户端侧共用同一常量。</summary>
    public const string HeaderName = GrpcAccessCredentials.HeaderName;
}
