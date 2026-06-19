using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Events;
using OhMyBot.Contracts.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OhMyBot.QQGateway;

public sealed class RouteRefreshConsumerService(
    QQCommandGateway gateway,
    IConfiguration configuration,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    ILogger<RouteRefreshConsumerService> logger) : BackgroundService
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
                logger.LogError(exception, "RabbitMQ route refresh consumer failed. Retrying in 10 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var botInstanceId = configuration["BotInstanceId"] ?? "qq-default";
        var queueName = string.IsNullOrWhiteSpace(_rabbitMqOptions.NotificationQueue)
            ? "ohmybot.qq.notifications"
            : _rabbitMqOptions.NotificationQueue;

        var factory = new ConnectionFactory
        {
            HostName = _rabbitMqOptions.HostName,
            Port = _rabbitMqOptions.Port,
            UserName = _rabbitMqOptions.UserName,
            Password = _rabbitMqOptions.Password,
            VirtualHost = _rabbitMqOptions.VirtualHost,
            DispatchConsumersAsync = false
        };

        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        channel.ExchangeDeclare(_rabbitMqOptions.NotificationExchange, ExchangeType.Topic, durable: true, autoDelete: false);
        channel.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind(queueName, _rabbitMqOptions.NotificationExchange, RouteChangedEvent.EventType);

        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += (_, args) =>
        {
            try
            {
                var routeEvent = JsonSerializer.Deserialize<RouteChangedEvent>(args.Body.Span, JsonOptions);
                if (routeEvent?.Type == RouteChangedEvent.EventType)
                {
                    var routes = gateway.ReloadAsync(botInstanceId, stoppingToken)
                        .GetAwaiter()
                        .GetResult();
                    logger.LogInformation("Reloaded {Count} QQ routes from route change event version {Version}.",
                        routes.Count, routeEvent.Version);
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to handle route change event.");
            }
            finally
            {
                channel.BasicAck(args.DeliveryTag, multiple: false);
            }
        };

        channel.BasicConsume(queueName, autoAck: false, consumer);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
