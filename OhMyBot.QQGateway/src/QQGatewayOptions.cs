namespace OhMyBot.QQGateway;

public sealed class QQGatewayOptions
{
    public string BotInstanceId { get; set; } = "qq-default";

    public string CoreGrpcAddress { get; set; } = "http://localhost:5100";

    public string[] CommandPrefixes { get; set; } = ["/", "!", "."];
}
