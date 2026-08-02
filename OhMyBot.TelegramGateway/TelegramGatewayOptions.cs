namespace OhMyBot.TelegramGateway;

public sealed class TelegramGatewayOptions
{
    public string BotInstanceId { get; set; } = "telegram-default";

    public string BotToken { get; set; } = string.Empty;

    public string HttpProxy { get; set; } = string.Empty;

    public string CoreGrpcAddress { get; set; } = "http://localhost:5100";

    /// <summary>Core gRPC 共享访问令牌；必须与 Core 的 Grpc:AccessToken 一致。</summary>
    public string CoreAccessToken { get; set; } = string.Empty;

    public bool DropPendingUpdates { get; set; } = true;

    public string[] CommandPrefixes { get; set; } = ["/", "!", "."];

    // 网关侧「最近已记录档案」去重时长：同一用户档案未变时，此时长内不再向 Core 重复发 RecordUserProfile。
    public TimeSpan ProfileRecordDedupTtl { get; set; } = TimeSpan.FromMinutes(30);
}
