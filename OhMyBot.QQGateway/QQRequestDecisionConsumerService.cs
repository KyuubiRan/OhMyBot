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

// 消费 Core 下发的平台审批决定，翻译成 OneBot 动作执行。
// Core 只表达「同意/拒绝哪一条请求」，OneBot 动作名与参数形态是平台细节，只在本类里出现。
public sealed class QQRequestDecisionConsumerService(
    IOneBotClient oneBotClient,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    ILogger<QQRequestDecisionConsumerService> logger) : BackgroundService
{
    private readonly RabbitMqOptions _rabbitMqOptions = rabbitMqOptions.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // OneBot 连接由 GatewayWorker 独占管理，这里只消费队列并复用已连接的客户端。
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
                logger.LogError(exception, "QQ 审批决定消费者失败，10 秒后重试。");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var queueName = string.IsNullOrWhiteSpace(_rabbitMqOptions.NotificationQueue)
            ? "ohmybot.qq.notifications.platform-requests"
            : _rabbitMqOptions.NotificationQueue + ".platform-requests";

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
            PlatformRequestDecisionEvent.QqEventType,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            try
            {
                var decision = JsonSerializer.Deserialize<PlatformRequestDecisionEvent>(args.Body.Span, JsonOptions);
                if (decision is { Type: PlatformRequestDecisionEvent.QqEventType, Platform: BotPlatform.Qq })
                {
                    await ApplyAsync(decision, stoppingToken);
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "处理 QQ 审批决定失败。");
            }
            finally
            {
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(queueName, autoAck: false, consumer, stoppingToken);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private async Task ApplyAsync(PlatformRequestDecisionEvent decision, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(decision.Flag))
        {
            logger.LogWarning("QQ 审批决定缺少 flag，已忽略。kind={Kind}", decision.Kind);
            return;
        }

        // 拒绝理由：好友请求 OneBot 无处安放（只有 remark 字段），仅群请求的 reason 会展示给申请人。
        var (action, parameters) = decision.Kind switch
        {
            PlatformRequestKind.FriendAdd => (
                "set_friend_add_request",
                (object)new { flag = decision.Flag, approve = decision.Approve }),
            PlatformRequestKind.GroupInvite => (
                "set_group_add_request",
                new
                {
                    flag = decision.Flag,
                    sub_type = "invite",
                    approve = decision.Approve,
                    reason = decision.Approve ? string.Empty : decision.Reason
                }),
            PlatformRequestKind.GroupAdd => (
                "set_group_add_request",
                new
                {
                    flag = decision.Flag,
                    sub_type = "add",
                    approve = decision.Approve,
                    reason = decision.Approve ? string.Empty : decision.Reason
                }),
            _ => (string.Empty, new object())
        };

        if (string.IsNullOrEmpty(action))
        {
            logger.LogWarning("未知的 QQ 审批请求类型，已忽略。kind={Kind}", decision.Kind);
            return;
        }

        var response = await oneBotClient.SendActionAsync(new OneBotActionRequest(action, parameters), cancellationToken);
        if (!response.IsSuccess)
        {
            logger.LogWarning(
                "OneBot {Action} 执行失败 retcode={RetCode} msg={Message}",
                action,
                response.RetCode,
                response.Message ?? response.Wording);
            return;
        }

        logger.LogInformation(
            "已执行 QQ 审批决定。kind={Kind} approve={Approve}",
            decision.Kind,
            decision.Approve);
    }
}
