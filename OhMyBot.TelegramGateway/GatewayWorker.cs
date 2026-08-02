using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace OhMyBot.TelegramGateway;

public sealed class GatewayWorker(
    ITelegramBotClient botClient,
    TelegramCommandGateway gateway,
    TelegramUpdateHandler updateHandler,
    IOptions<TelegramGatewayOptions> options,
    ILogger<GatewayWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
    private readonly TelegramGatewayOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken))
        {
            throw new InvalidOperationException("Telegram:BotToken is required.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Telegram gateway startup failed. Retrying in {DelaySeconds} seconds.",
                    RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        var me = await botClient.GetMe(stoppingToken);
        logger.LogInformation("Telegram gateway connected as @{Username} ({BotId}).", me.Username, me.Id);
        await RegisterSelfProfileAsync(me, stoppingToken);
        logger.LogInformation("Telegram drop pending updates: {DropPendingUpdates}.", _options.DropPendingUpdates);

        await botClient.DeleteWebhook(dropPendingUpdates: _options.DropPendingUpdates, cancellationToken: stoppingToken);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery],
            DropPendingUpdates = _options.DropPendingUpdates
        };

        botClient.StartReceiving(updateHandler, receiverOptions, stoppingToken);

        try
        {
            var commands = await gateway.ReloadAsync(_options.BotInstanceId, stoppingToken);
            logger.LogInformation("Telegram gateway loaded {Count} commands from Core.", commands.Count);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to load Telegram routes from Core. The gateway will keep running; use /reload after Core is reachable.");
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    // 登记 bot 自身档案：bot 收不到自己的消息，否则 /info、/setpriv 以自身为目标时只会显示 uid。
    // 复用启动时的 GetMe 结果登记一次；失败仅告警，不影响网关运行。
    private async Task RegisterSelfProfileAsync(Telegram.Bot.Types.User me, CancellationToken stoppingToken)
    {
        try
        {
            await gateway.RecordUserProfileAsync(
                new GatewayCommandRequest(
                    me.Id.ToString(),
                    me.Id.ToString(),
                    string.Empty,
                    string.Empty,
                    Username: me.Username,
                    FirstName: me.FirstName,
                    LastName: me.LastName),
                _options.BotInstanceId,
                stoppingToken);
            logger.LogInformation("已登记 Telegram bot 自身档案。uid={BotId}", me.Id);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "登记 Telegram bot 自身档案失败。");
        }
    }
}
