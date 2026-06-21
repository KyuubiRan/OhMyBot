using Microsoft.Extensions.Options;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Kuro;
using OhMyBot.Core.Messaging;
using OhMyBot.Core.Notifications;

namespace OhMyBot.Core.ScheduledTasks;

public sealed class KuroAutoSignManagedTask : ManagedTaskBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KuroAutoSignManagedTask> _logger;

    public KuroAutoSignManagedTask(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ScheduledTaskOptions> options,
        TimeProvider timeProvider,
        ILogger<KuroAutoSignManagedTask> logger)
        : base(options.Get("KuroAutoSign").Enabled, options.Get("KuroAutoSign").Cron, timeProvider)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public override string Name => "kuro-auto-sign";

    public override string Description => "Kuro BBS automatic sign in.";

    protected override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        var offset = 0;
        const int limit = 20;
        while (!cancellationToken.IsCancellationRequested)
        {
            List<long> targetIds;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
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
        var accountService = scope.ServiceProvider.GetRequiredService<KuroAccountService>();
        var signService = scope.ServiceProvider.GetRequiredService<KuroSignService>();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<NotificationSubscriptionService>();
        var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
        var account = await accountService.FindByIdAsync(accountId, noTracking: true, cancellationToken);
        if (account is null)
        {
            return;
        }

        var telegramEndpoints = await subscriptionService.ListEnabledEndpointsByTargetAsync(
            BotPlatform.Telegram,
            NotificationTypes.KuroAutoSign,
            account.Id,
            cancellationToken);
        KuroAutoSignResult result;
        try
        {
            result = await signService.ExecuteAutoSignAsync(account, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to process Kuro account {AccountId}.", account.Id);
            var error = $"[库街区-自动签到]\n账号：{account.DisplayName}\n自动签到执行失败：{exception.GetBaseException().Message}";
            await PublishAsync(publisher, telegramEndpoints, error, cancellationToken);
            return;
        }

        if (!result.HasResult)
        {
            return;
        }

        await PublishAsync(publisher, telegramEndpoints, FormatNotification(result, TimeProvider.GetUtcNow()), cancellationToken);
    }

    private static async Task PublishAsync(
        INotificationPublisher publisher,
        IReadOnlyList<NotificationEndpoint> telegramEndpoints,
        string message,
        CancellationToken cancellationToken)
    {
        foreach (var endpoint in telegramEndpoints)
        {
            await publisher.PublishTelegramAsync(endpoint.BotInstanceId, endpoint.ChatId, [message], cancellationToken);
        }
    }

    private static string FormatNotification(KuroAutoSignResult result, DateTimeOffset time)
    {
        var lines = new List<string>
        {
            "[库街区-自动签到]",
            $"账号：{result.Account.DisplayName} ({result.Account.BbsUserId})"
        };
        lines.AddRange(result.Lines);
        lines.Add("时间：" + time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        return string.Join('\n', lines);
    }
}
