using Microsoft.Extensions.Options;
using OhMyBot.Core.Commanding.Notifications;
using OhMyBot.Core.Infrastructure.Messaging;
using OhMyBot.Core.Integrations.Skland;

namespace OhMyBot.Core.Infrastructure.ScheduledTasks;

public sealed class SklandAutoSignManagedTask : ManagedTaskBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SklandAutoSignManagedTask> _logger;

    public SklandAutoSignManagedTask(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ScheduledTaskOptions> options,
        TimeProvider timeProvider,
        ILogger<SklandAutoSignManagedTask> logger)
        : base(options.Get("SklandAutoSign").Enabled, options.Get("SklandAutoSign").Cron, timeProvider)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public override string Name => "skland-auto-sign";

    public override string Description => "Skland daily attendance automatic sign in.";

    protected override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        var offset = 0;
        const int limit = 20;
        while (!cancellationToken.IsCancellationRequested)
        {
            List<long> targetIds;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var accountService = scope.ServiceProvider.GetRequiredService<SklandAccountService>();
                var accounts = await accountService.ListAutoSignTargetsAsync(offset, limit, cancellationToken);
                targetIds = accounts.Select(a => a.Id).ToList();
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
        var accountService = scope.ServiceProvider.GetRequiredService<SklandAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<SklandSignService>();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<NotificationSubscriptionService>();
        var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();

        var account = await accountService.FindByIdAsync(accountId, noTracking: true, cancellationToken);
        if (account is null || string.IsNullOrEmpty(account.CredCiphertext))
        {
            return;
        }

        var deliveries = await subscriptionService.ListEnabledDeliveriesByTargetAsync(
            NotificationTypes.SklandAutoSign,
            account.Id,
            cancellationToken);

        SklandAutoSignResult result;
        try
        {
            result = await signService.ExecuteAutoSignAsync(account, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to process Skland account {AccountId}.", account.Id);
            var error = $"[森空岛-自动签到]\n账号：{account.DisplayName}\n自动签到执行失败：{exception.GetBaseException().Message}";
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

    private static string FormatNotification(SklandAutoSignResult result, DateTimeOffset time)
    {
        var lines = new List<string>
        {
            "[森空岛-自动签到]",
            $"账号：{result.Account.DisplayName} ({result.Account.SklandUserId})"
        };
        lines.AddRange(result.Lines);
        lines.Add("时间：" + time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        return string.Join('\n', lines);
    }
}
