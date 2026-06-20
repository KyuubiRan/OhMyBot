using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Events;
using OhMyBot.Contracts.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OhMyBot.TelegramGateway;

public sealed class RouteRefreshConsumerService(
    TelegramCommandGateway gateway,
    IOptions<TelegramGatewayOptions> gatewayOptions,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    ILogger<RouteRefreshConsumerService> logger) : BackgroundService
{
    private readonly TelegramGatewayOptions _gatewayOptions = gatewayOptions.Value;
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
                logger.LogError(exception, "RabbitMQ route refresh consumer failed. Retrying in 10 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var queueName = string.IsNullOrWhiteSpace(_rabbitMqOptions.NotificationQueue)
            ? "ohmybot.telegram.notifications"
            : _rabbitMqOptions.NotificationQueue;

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
            RouteChangedEvent.EventType,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            try
            {
                var routeEvent = JsonSerializer.Deserialize<RouteChangedEvent>(args.Body.Span, JsonOptions);
                if (routeEvent?.Type == RouteChangedEvent.EventType)
                {
                    var routes = await gateway.ReloadAsync(_gatewayOptions.BotInstanceId, stoppingToken);
                    logger.LogInformation("Reloaded {Count} Telegram routes from route change event version {Version}.",
                        routes.Count, routeEvent.Version);
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to handle route change event.");
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
