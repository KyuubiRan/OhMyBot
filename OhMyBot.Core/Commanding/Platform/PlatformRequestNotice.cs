using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Commanding.Platform;

/// <summary>
/// 平台侧一条待人工审批的请求。Core 不解释 <paramref name="Flag"/> 的内容，
/// 它是网关用来回执审批结果的平台句柄。
/// </summary>
/// <param name="RequesterProfile">
/// 申请人档案补充字段（键见 <c>PlatformRequestProfileKeys</c>）。Core 只透传，由插件决定怎么展示。
/// </param>
public sealed record PlatformRequestNotice(
    BotPlatform Platform,
    string BotInstanceId,
    PlatformRequestKind Kind,
    string Flag,
    string RequesterId,
    string RequesterName,
    string GroupId,
    string Comment,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, string>? RequesterProfile = null);
