using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Events;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Contracts.Messaging;
using OhMyBot.OneBotV11;
using OhMyBot.OneBotV11.Transport;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OhMyBot.QQGateway;

public sealed class QQNotificationConsumerService(
    IOneBotClient oneBotClient,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    ILogger<QQNotificationConsumerService> logger) : BackgroundService
{
    private readonly RabbitMqOptions _rabbitMqOptions = rabbitMqOptions.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await oneBotClient.StartAsync(stoppingToken);
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "QQ notification consumer failed. Retrying in 10 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            finally
            {
                await oneBotClient.StopAsync(CancellationToken.None);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var queueName = string.IsNullOrWhiteSpace(_rabbitMqOptions.NotificationQueue)
            ? "ohmybot.qq.notifications.messages"
            : _rabbitMqOptions.NotificationQueue + ".messages";

        var factory = new ConnectionFactory
        {
            HostName = _rabbitMqOptions.HostName,
            Port = _rabbitMqOptions.Port,
            UserName = _rabbitMqOptions.UserName,
            Password = _rabbitMqOptions.Password,
            VirtualHost = _rabbitMqOptions.VirtualHost
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.ExchangeDeclareAsync(
            _rabbitMqOptions.NotificationExchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.QueueBindAsync(
            queueName,
            _rabbitMqOptions.NotificationExchange,
            BotNotificationEvent.QqEventType,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            try
            {
                var notification = JsonSerializer.Deserialize<BotNotificationEvent>(args.Body.Span, JsonOptions);
                if (notification is { Type: BotNotificationEvent.QqEventType, Platform: BotPlatform.Qq })
                {
                    await SendMessagesAsync(notification, stoppingToken);
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to handle QQ notification event.");
            }
            finally
            {
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(queueName, autoAck: false, consumer, stoppingToken);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private async Task SendMessagesAsync(BotNotificationEvent notification, CancellationToken cancellationToken)
    {
        if (!long.TryParse(notification.ChatId, out var userId))
        {
            logger.LogWarning("Invalid QQ user id for notification: {ChatId}.", notification.ChatId);
            return;
        }

        foreach (var message in notification.Messages.Where(message => !string.IsNullOrWhiteSpace(message)))
        {
            var response = await oneBotClient.SendActionAsync(
                new OneBotActionRequest("send_private_msg", new { user_id = userId, message }),
                cancellationToken);

            if (!response.IsSuccess)
            {
                logger.LogWarning("OneBot send_private_msg failed retcode={RetCode} message={Message}.",
                    response.RetCode,
                    response.Message ?? response.Wording);
            }
        }
    }
}
