using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OhMyBot.QQGateway;

public sealed class GatewayWorker(
    QQCommandGateway gateway,
    IConfiguration configuration,
    ILogger<GatewayWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var botInstanceId = configuration["BotInstanceId"] ?? "qq-default";
        var commands = await gateway.ReloadAsync(botInstanceId, stoppingToken);
        logger.LogInformation("QQ gateway loaded {Count} commands from Core.", commands.Count);

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
