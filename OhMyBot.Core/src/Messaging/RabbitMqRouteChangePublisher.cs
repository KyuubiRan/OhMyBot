using System.Text.Json;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Events;
using OhMyBot.Contracts.Messaging;
using RabbitMQ.Client;

namespace OhMyBot.Core.Messaging;

public sealed class RabbitMqRouteChangePublisher(
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider,
    ILogger<RabbitMqRouteChangePublisher> logger) : IRouteChangePublisher
{
    private readonly RabbitMqOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishRoutesChangedAsync(long version, CancellationToken cancellationToken = default)
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
                RouteChangedEvent.Create(version, timeProvider.GetUtcNow()),
                JsonOptions);

            var properties = new BasicProperties
            {
                ContentType = "application/json",
                Persistent = true
            };

            await channel.BasicPublishAsync(
                _options.NotificationExchange,
                RouteChangedEvent.EventType,
                mandatory: false,
                basicProperties: properties,
                body: payload,
                cancellationToken: cancellationToken);

            logger.LogInformation("Published route change event version {Version}.", version);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to publish route change event.");
        }
    }
}
