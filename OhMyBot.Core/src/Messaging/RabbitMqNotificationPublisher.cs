using System.Text.Json;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Events;
using OhMyBot.Contracts.Messaging;
using RabbitMQ.Client;

namespace OhMyBot.Core.Messaging;

public sealed class RabbitMqNotificationPublisher(
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider,
    ILogger<RabbitMqNotificationPublisher> logger) : INotificationPublisher
{
    private readonly RabbitMqOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishTelegramAsync(
        string botInstanceId,
        string chatId,
        IReadOnlyList<string> messages,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost
            };

            await using var connection = await factory.CreateConnectionAsync(cancellationToken);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await channel.ExchangeDeclareAsync(
                _options.NotificationExchange,
                ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);

            var payload = JsonSerializer.SerializeToUtf8Bytes(
                BotNotificationEvent.Telegram(botInstanceId, chatId, messages, timeProvider.GetUtcNow()),
                JsonOptions);

            await channel.BasicPublishAsync(
                _options.NotificationExchange,
                BotNotificationEvent.EventType,
                mandatory: false,
                basicProperties: new BasicProperties
                {
                    ContentType = "application/json",
                    Persistent = true
                },
                body: payload,
                cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to publish Telegram notification.");
        }
    }
}
