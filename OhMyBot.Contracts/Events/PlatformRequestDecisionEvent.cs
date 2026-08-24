using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Contracts.Events;

/// <summary>
/// Core 对平台待审批请求的处理决定，经消息总线下发给网关执行。
/// 只带语义（同意/拒绝 + 请求类型 + 平台句柄），具体协议动作由网关翻译，
/// 避免 Core 侧获得「让网关执行任意平台动作」的能力。
/// </summary>
public sealed record PlatformRequestDecisionEvent(
    string Type,
    BotPlatform Platform,
    string BotInstanceId,
    PlatformRequestKind Kind,
    string Flag,
    bool Approve,
    string Reason,
    DateTimeOffset CreatedAt)
{
    public const string QqEventType = "platform-request-decision.qq";

    public static string GetEventType(BotPlatform platform)
    {
        return platform switch
        {
            BotPlatform.Qq => QqEventType,
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported platform request decision platform.")
        };
    }

    public static PlatformRequestDecisionEvent Create(
        BotPlatform platform,
        string botInstanceId,
        PlatformRequestKind kind,
        string flag,
        bool approve,
        string reason,
        DateTimeOffset createdAt)
    {
        return new PlatformRequestDecisionEvent(
            GetEventType(platform),
            platform,
            botInstanceId,
            kind,
            flag,
            approve,
            reason,
            createdAt);
    }
}
