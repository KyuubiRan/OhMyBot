using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyBot.OneBotV11;

namespace OhMyBot.QQGateway;

// QQ 网关主机：加载路由、订阅 OneBot 消息事件、维护到 NapCat 的连接生命周期。
// 连接生命周期由本服务独占，避免与通知消费服务相互干扰。
public sealed class GatewayWorker(
    QQCommandGateway gateway,
    QQUpdateHandler updateHandler,
    IOneBotClient oneBotClient,
    IConfiguration configuration,
    ILogger<GatewayWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var botInstanceId = configuration["BotInstanceId"] ?? "qq-default";

        oneBotClient.OnEvent += updateHandler.Handle;
        oneBotClient.OnException += HandleOneBotException;

        try
        {
            // NapCat 与 Core 任一尚未就绪时都等待重试，而不是让网关直接退出。
            // 先连 NapCat（传输层自带自动重连），再从 Core 拉取路由；Core 恢复后自愈。
            await ConnectWithRetryAsync(stoppingToken);
            await LoadRoutesWithRetryAsync(botInstanceId, stoppingToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            oneBotClient.OnEvent -= updateHandler.Handle;
            oneBotClient.OnException -= HandleOneBotException;
            await oneBotClient.StopAsync(CancellationToken.None);
        }
    }

    private async Task LoadRoutesWithRetryAsync(string botInstanceId, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var commands = await gateway.ReloadAsync(botInstanceId, stoppingToken);
                logger.LogInformation("QQ gateway loaded {Count} commands from Core.", commands.Count);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to load routes from Core. Retrying in 5 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConnectWithRetryAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 连上后传输层自带自动重连，这里只负责扛住 NapCat 尚未就绪时的首次连接失败。
                await oneBotClient.StartAsync(stoppingToken);
                logger.LogInformation("Connected to OneBot endpoint. transport={Transport}", oneBotClient.TransportType);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to connect to OneBot endpoint. Retrying in 5 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private void HandleOneBotException(Exception exception)
    {
        logger.LogError(exception, "OneBot client error.");
    }
}
