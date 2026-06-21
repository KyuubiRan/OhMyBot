using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.AiRouter;
using OhMyBot.Core.Messaging;
using OhMyBot.Core.Notifications;

namespace OhMyBot.Core.ScheduledTasks;

public sealed class AiRouterAutoSignManagedTask : ManagedTaskBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AiRouterAutoSignManagedTask> _logger;

    public AiRouterAutoSignManagedTask(
        IServiceScopeFactory scopeFactory,
        IOptions<ScheduledTaskOptions> options,
        TimeProvider timeProvider,
        ILogger<AiRouterAutoSignManagedTask> logger)
        : base(options.Value.Enabled, options.Value.Cron, timeProvider)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public override string Name => "ai-router-auto-sign";

    public override string Description => "AI Router automatic sign in.";

    protected override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        var offset = 0;
        const int limit = 20;

        while (!cancellationToken.IsCancellationRequested)
        {
            List<long> targetIds;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var accountService = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
                var accounts = await accountService.ListAutoSignTargetsAsync(offset, limit, cancellationToken);
                targetIds = accounts.Select(account => account.Id).ToList();
            }

            if (targetIds.Count == 0)
            {
                return;
            }

            offset += limit;
            foreach (var accountId in targetIds)
            {
                await ProcessSingleAsync(accountId, cancellationToken);
            }

            if (targetIds.Count < limit)
            {
                return;
            }
        }
    }

    private async Task ProcessSingleAsync(long accountId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<AiRouterAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<AiRouterSignService>();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<NotificationSubscriptionService>();
        var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
        var account = await accountService.FindByIdAsync(accountId, noTracking: true, cancellationToken);
        if (account is null)
        {
            return;
        }

        var telegramEndpoints = await subscriptionService.ListEnabledEndpointsByTargetAsync(
            BotPlatform.Telegram,
            NotificationTypes.AiRouterAutoSign,
            account.Id,
            cancellationToken);

        AiRouterSignResult result;
        try
        {
            result = await signService.SignInAsync(account, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to process AI Router account {AccountId}.", account.Id);
            foreach (var endpoint in telegramEndpoints)
            {
                await publisher.PublishTelegramAsync(
                    endpoint.BotInstanceId,
                    endpoint.ChatId,
                    [$"[AI Router-自动签到]\n账号：{account.DisplayName}\n自动签到执行失败：{exception.GetBaseException().Message}"],
                    cancellationToken);
            }

            return;
        }

        if (telegramEndpoints.Count == 0)
        {
            return;
        }

        var message = FormatNotification(result, TimeProvider.GetUtcNow());
        foreach (var endpoint in telegramEndpoints)
        {
            await publisher.PublishTelegramAsync(endpoint.BotInstanceId, endpoint.ChatId, [message], cancellationToken);
        }
    }

    private static string FormatNotification(AiRouterSignResult result, DateTimeOffset time)
    {
        var status = result.Type switch
        {
            AiRouterSignResultType.Success => "签到成功",
            AiRouterSignResultType.AlreadySigned => "今日已签到",
            _ => "签到失败"
        };
        var lines = new List<string>
        {
            "[AI Router-自动签到]",
            $"账号：{result.DisplayName}",
            $"结果：{status}",
            $"说明：{result.Message}"
        };

        if (result.SignIn is not null)
        {
            lines.Add($"今日奖励：{result.SignIn.TodayReward:F2}");
            lines.Add($"连续签到：{result.SignIn.CurrentStreak} 天");
            lines.Add($"累计奖励：{result.SignIn.TotalReward:F2}");
            lines.Add($"本月签到：{result.SignIn.MonthSignedDays} 天");
        }

        lines.Add("时间：" + time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        return string.Join('\n', lines);
    }
}
