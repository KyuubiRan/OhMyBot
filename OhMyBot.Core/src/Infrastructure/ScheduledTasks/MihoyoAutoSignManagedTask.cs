using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Infrastructure.Messaging;
using OhMyBot.Core.Integrations.Mihoyo;
using OhMyBot.Core.Commanding.Notifications;

namespace OhMyBot.Core.Infrastructure.ScheduledTasks;

public sealed class MihoyoAutoSignManagedTask : ManagedTaskBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MihoyoAutoSignManagedTask> _logger;

    public MihoyoAutoSignManagedTask(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ScheduledTaskOptions> options,
        TimeProvider timeProvider,
        ILogger<MihoyoAutoSignManagedTask> logger)
        : base(options.Get("MihoyoAutoSign").Enabled, options.Get("MihoyoAutoSign").Cron, timeProvider)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public override string Name => "mihoyo-auto-sign";

    public override string Description => "Mihoyo BBS automatic sign in.";

    protected override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        var offset = 0;
        const int limit = 20;
        while (!cancellationToken.IsCancellationRequested)
        {
            List<long> targetIds;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
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
        var accountService = scope.ServiceProvider.GetRequiredService<MihoyoAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<MihoyoSignService>();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<NotificationSubscriptionService>();
        var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
        var account = await accountService.FindByIdAsync(accountId, noTracking: true, cancellationToken);
        if (account is null || string.IsNullOrEmpty(account.CookieCiphertext))
        {
            return;
        }

        var deliveries = await subscriptionService.ListEnabledDeliveriesByTargetAsync(
            NotificationTypes.MihoyoAutoSign,
            account.Id,
            cancellationToken);
        MihoyoAutoSignResult result;
        try
        {
            result = await signService.ExecuteAutoSignAsync(account, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to process Mihoyo account {AccountId}.", account.Id);
            var error = $"[米游社-自动签到]\n账号：{account.DisplayName}\n自动签到执行失败：{exception.GetBaseException().Message}";
            await PublishAsync(publisher, deliveries, error, cancellationToken);
            return;
        }

        if (!result.HasResult)
        {
            return;
        }

        await PublishAsync(publisher, deliveries, FormatNotification(result, TimeProvider.GetUtcNow()), cancellationToken);
    }

    private static async Task PublishAsync(
        INotificationPublisher publisher,
        IReadOnlyList<NotificationDelivery> deliveries,
        string message,
        CancellationToken cancellationToken)
    {
        foreach (var delivery in deliveries)
        {
            await publisher.PublishAsync(delivery.Platform, delivery.BotInstanceId, delivery.ChatId, [message], cancellationToken);
        }
    }

    private static string FormatNotification(MihoyoAutoSignResult result, DateTimeOffset time)
    {
        var regionLabel = result.Account.Region == Data.Entities.MihoyoRegion.Cn ? "国服" : "国际服";
        var lines = new List<string>
        {
            "[米游社-自动签到]",
            $"账号：{result.Account.DisplayName} ({result.Account.Stuid}) [{regionLabel}]"
        };
        lines.AddRange(result.Lines);
        lines.Add("时间：" + time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        return string.Join('\n', lines);
    }
}
