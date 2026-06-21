using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Events;
using OhMyBot.Contracts.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Telegram.Bot;

namespace OhMyBot.TelegramGateway;

public sealed class TelegramNotificationConsumerService(
    ITelegramBotClient botClient,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    ILogger<TelegramNotificationConsumerService> logger) : BackgroundService
{
    private readonly RabbitMqOptions _rabbitMqOptions = rabbitMqOptions.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                logger.LogError(exception, "RabbitMQ notification consumer failed. Retrying in 10 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var queueName = string.IsNullOrWhiteSpace(_rabbitMqOptions.NotificationQueue)
            ? "ohmybot.telegram.notifications"
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
            BotNotificationEvent.EventType,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            try
            {
                var notification = JsonSerializer.Deserialize<BotNotificationEvent>(args.Body.Span, JsonOptions);
                if (notification?.Type == BotNotificationEvent.EventType)
                {
                    foreach (var message in notification.Messages.Where(message => !string.IsNullOrWhiteSpace(message)))
                    {
                        await botClient.SendMessage(notification.ChatId, message, cancellationToken: stoppingToken);
                    }
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to handle Telegram notification event.");
            }
            finally
            {
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(queueName, autoAck: false, consumer, stoppingToken);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
