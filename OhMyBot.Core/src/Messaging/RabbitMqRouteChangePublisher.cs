using System.Text;
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

    public Task PublishRoutesChangedAsync(long version, CancellationToken cancellationToken = default)
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

            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();
            channel.ExchangeDeclare(_options.NotificationExchange, ExchangeType.Topic, durable: true, autoDelete: false);

            var payload = JsonSerializer.SerializeToUtf8Bytes(
                RouteChangedEvent.Create(version, timeProvider.GetUtcNow()),
                JsonOptions);

            var properties = channel.CreateBasicProperties();
            properties.ContentType = "application/json";
            properties.DeliveryMode = 2;

            channel.BasicPublish(
                _options.NotificationExchange,
                RouteChangedEvent.EventType,
                mandatory: false,
                basicProperties: properties,
                body: payload);

            logger.LogInformation("Published route change event version {Version}.", version);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to publish route change event.");
        }

        return Task.CompletedTask;
    }
}
