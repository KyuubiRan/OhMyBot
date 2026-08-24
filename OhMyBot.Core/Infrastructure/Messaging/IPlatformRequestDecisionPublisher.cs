using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Infrastructure.Messaging;

/// <summary>
/// 把平台待审批请求的处理决定下发给对应网关执行。
/// </summary>
public interface IPlatformRequestDecisionPublisher
{
    Task PublishAsync(
        BotPlatform platform,
        string botInstanceId,
        PlatformRequestKind kind,
        string flag,
        bool approve,
        string reason,
        CancellationToken cancellationToken = default);
}
