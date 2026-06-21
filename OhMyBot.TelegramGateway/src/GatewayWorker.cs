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
}
