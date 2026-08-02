using System.Text.Json;
using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Events;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Contracts.Messaging;
using RabbitMQ.Client;

namespace OhMyBot.Core.Infrastructure.Messaging;

/// <summary>
/// 通知发布器（Singleton）。自动签到一轮会按 delivery 逐条调用本类，
/// 因此连接和 channel 惰性建立后复用，只在首次建立时 declare exchange；
/// 发送失败即丢弃当前连接，下次调用重建。
/// </summary>
public sealed class RabbitMqNotificationPublisher(
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider,
    ILogger<RabbitMqNotificationPublisher> logger) : INotificationPublisher, IAsyncDisposable
{
    private readonly RabbitMqOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;
    private bool _disposed;

    public async Task PublishAsync(
        BotPlatform platform,
        string botInstanceId,
        string chatId,
        IReadOnlyList<string> messages,
        CancellationToken cancellationToken = default)
    {
        await PublishCoreAsync(
            BotNotificationEvent.Create(platform, botInstanceId, chatId, messages, timeProvider.GetUtcNow()),
            cancellationToken);
    }

    public async Task PublishTelegramAsync(
        string botInstanceId,
        string chatId,
        IReadOnlyList<string> messages,
        CancellationToken cancellationToken = default)
    {
        await PublishAsync(BotPlatform.Telegram, botInstanceId, chatId, messages, cancellationToken);
    }

    private async Task PublishCoreAsync(BotNotificationEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var channel = await EnsureChannelAsync(cancellationToken);
            var payload = JsonSerializer.SerializeToUtf8Bytes(notification, JsonOptions);

            await channel.BasicPublishAsync(
                _options.NotificationExchange,
                notification.Type,
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
            // 连接可能已断开，丢弃当前连接让下次调用重建，避免卡在坏连接上。
            await ResetConnectionAsync();
            logger.LogError(exception, "Failed to publish {Platform} notification.", notification.Platform);
        }
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true } existing)
        {
            return existing;
        }

        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_channel is { IsOpen: true } current)
            {
                return current;
            }

            await CloseAsync();

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await _channel.ExchangeDeclareAsync(
                _options.NotificationExchange,
                ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);
            return _channel;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private async Task ResetConnectionAsync()
    {
        await _connectionGate.WaitAsync();
        try
        {
            await CloseAsync();
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private async Task CloseAsync()
    {
        if (_channel is not null)
        {
            try
            {
                await _channel.DisposeAsync();
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Failed to dispose RabbitMQ channel.");
            }

            _channel = null;
        }

        if (_connection is not null)
        {
            try
            {
                await _connection.DisposeAsync();
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Failed to dispose RabbitMQ connection.");
            }

            _connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await ResetConnectionAsync();
        _connectionGate.Dispose();
    }
}
