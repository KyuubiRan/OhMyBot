using Microsoft.Extensions.Options;
using OhMyBot.Core.Commanding.Notifications;
using OhMyBot.Core.Infrastructure.Messaging;
using OhMyBot.Core.Integrations.Happytuk;

namespace OhMyBot.Core.Infrastructure.ScheduledTasks;

public sealed class HappytukAutoRedeemManagedTask : ManagedTaskBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HappytukAutoRedeemManagedTask> _logger;
    private readonly string? _couponFormat;

    public HappytukAutoRedeemManagedTask(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ScheduledTaskOptions> options,
        TimeProvider timeProvider,
        ILogger<HappytukAutoRedeemManagedTask> logger)
        : base(options.Get("HappytukAutoRedeem").Enabled, options.Get("HappytukAutoRedeem").Cron, timeProvider)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _couponFormat = options.Get("HappytukAutoRedeem").Args["CouponCode"]?.GetValue<string>();
    }

    public override string Name => "happytuk-auto-redeem";

    public override string Description => "HappyTuk weekly maintenance coupon automatic redemption.";

    protected override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_couponFormat))
        {
            throw new InvalidOperationException("ScheduledTasks:HappytukAutoRedeem:Args:CouponCode is required.");
        }

        var couponCode = TimeProvider.GetLocalNow().ToString(_couponFormat);
        var offset = 0;
        const int limit = 20;
        while (!cancellationToken.IsCancellationRequested)
        {
            List<long> accountIds;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var accounts = await scope.ServiceProvider.GetRequiredService<HappytukAccountService>()
                    .ListAutoRedeemTargetsAsync(offset, limit, cancellationToken);
                accountIds = accounts.Select(account => account.Id).ToList();
            }

            if (accountIds.Count == 0)
            {
                return;
            }

            offset += limit;
            foreach (var accountId in accountIds)
            {
                await ProcessSingleAsync(accountId, couponCode, cancellationToken);
            }

            if (accountIds.Count < limit)
            {
                return;
            }
        }
    }

    private async Task ProcessSingleAsync(long accountId, string couponCode, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var accountService = scope.ServiceProvider.GetRequiredService<HappytukAccountService>();
        var redeemService = scope.ServiceProvider.GetRequiredService<HappytukRedeemService>();
        var subscriptionService = scope.ServiceProvider.GetRequiredService<NotificationSubscriptionService>();
        var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
        var account = await accountService.FindByIdAsync(accountId, cancellationToken);
        if (account is null)
        {
            return;
        }

        var deliveries = await subscriptionService.ListEnabledDeliveriesByTargetAsync(
            NotificationTypes.HappytukAutoRedeem,
            account.Id,
            cancellationToken);
        string message;
        try
        {
            var result = await redeemService.RedeemAsync(account, couponCode, cancellationToken);
            message = $"[HappyTuk-自动兑换]\n账号：{account.LoginAccount}\n兑换码：{couponCode}\n结果：{result.Message}";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to redeem HappyTuk coupon for account {AccountId}.", account.Id);
            message = $"[HappyTuk-自动兑换]\n账号：{account.LoginAccount}\n兑换码：{couponCode}\n结果：兑换失败（{exception.GetBaseException().Message}）";
        }

        message += "\n时间：" + TimeProvider.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss");
        foreach (var delivery in deliveries)
        {
            await publisher.PublishAsync(delivery.Platform, delivery.BotInstanceId, delivery.ChatId, [message], cancellationToken);
        }
    }
}
