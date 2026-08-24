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
    QQCommandGateway gateway,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    ILogger<QQNotificationConsumerService> logger) : BackgroundService
{
    private readonly RabbitMqOptions _rabbitMqOptions = rabbitMqOptions.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // OneBot 连接由 GatewayWorker 独占管理，这里只消费队列并复用已连接的客户端发送。
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
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

        for (var index = 0; index < notification.Messages.Count; index++)
        {
            var message = notification.Messages[index];
            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            var response = await oneBotClient.SendActionAsync(
                new OneBotActionRequest("send_private_msg", new { user_id = userId, message }),
                cancellationToken);

            if (!response.IsSuccess)
            {
                logger.LogWarning("OneBot send_private_msg failed retcode={RetCode} message={Message}.",
                    response.RetCode,
                    response.Message ?? response.Wording);
                continue;
            }

            // 带菜单的通知（如待审批请求）发出后要把「消息 id -> 选项」绑回 Core，
            // 否则收件人回复序号时 Core 查不到菜单，只能静默丢弃。
            var menuToken = notification.MenuTokens is { } tokens && index < tokens.Count ? tokens[index] : null;
            if (string.IsNullOrEmpty(menuToken))
            {
                continue;
            }

            var messageId = ExtractMessageId(response.Data);
            if (string.IsNullOrEmpty(messageId))
            {
                logger.LogWarning("QQ 通知菜单无法绑定：send_private_msg 未返回 message_id。chatId={ChatId}", notification.ChatId);
                continue;
            }

            try
            {
                await gateway.BindMenuAsync(
                    notification.ChatId,
                    messageId,
                    notification.ChatId,
                    BotChatType.Private,
                    menuToken,
                    notification.BotInstanceId,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "绑定 QQ 通知菜单失败。message_id={MessageId}", messageId);
            }
        }
    }

    private static string? ExtractMessageId(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("message_id", out var element))
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.String => element.GetString(),
                _ => null
            };
        }

        return null;
    }
}
