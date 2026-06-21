using Microsoft.EntityFrameworkCore;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Data;
using OhMyBot.Core.Data.Entities;

namespace OhMyBot.Core.Notifications;

public sealed class NotificationSubscriptionService(
    OhMyBotV2DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<HashSet<long>> GetEnabledTargetIdsAsync(
        long coreUserId,
        BotPlatform platform,
        string notificationType,
        IReadOnlyCollection<long> knownTargetIds,
        CancellationToken cancellationToken = default)
    {
        var platformFlag = ToFlag(platform);
        var subscriptions = await dbContext.NotificationSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.CoreUserId == coreUserId
                && subscription.NotificationType == notificationType
                && knownTargetIds.Contains(subscription.TargetId))
            .ToListAsync(cancellationToken);
        var byTarget = subscriptions.ToDictionary(subscription => subscription.TargetId);
        return knownTargetIds
            .Where(targetId => !byTarget.TryGetValue(targetId, out var subscription)
                || HasPlatform(subscription.EnabledPlatforms, platformFlag))
            .ToHashSet();
    }

    public async Task<List<NotificationEndpoint>> ListEnabledEndpointsByTargetAsync(
        BotPlatform platform,
        string notificationType,
        long targetId,
        CancellationToken cancellationToken = default)
    {
        var platformFlag = ToFlag(platform);
        var subscriptions = await dbContext.NotificationSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.NotificationType == notificationType
                && subscription.TargetId == targetId
                && (subscription.EnabledPlatforms & (int)platformFlag) != 0)
            .ToListAsync(cancellationToken);

        return subscriptions
            .Select(subscription => ToEndpoint(subscription, platform))
            .Where(endpoint => endpoint is not null)
            .Cast<NotificationEndpoint>()
            .ToList();
    }

    public async Task ToggleAsync(
        long coreUserId,
        BotPlatform platform,
        string botInstanceId,
        string chatId,
        string notificationType,
        long targetId,
        CancellationToken cancellationToken = default)
    {
        var subscription = await dbContext.NotificationSubscriptions
            .FirstOrDefaultAsync(item => item.CoreUserId == coreUserId
                && item.NotificationType == notificationType
                && item.TargetId == targetId,
                cancellationToken);
        var platformFlag = ToFlag(platform);
        var now = timeProvider.GetUtcNow();
        if (subscription is null)
        {
            subscription = new NotificationSubscription
            {
                CoreUserId = coreUserId,
                NotificationType = notificationType,
                TargetId = targetId,
                EnabledPlatforms = (int)(NotificationPlatformFlags.All & ~platformFlag),
                CreatedAt = now,
                UpdatedAt = now
            };
            SetEndpoint(subscription, platform, botInstanceId, chatId);
            dbContext.NotificationSubscriptions.Add(subscription);
        }
        else
        {
            SetEndpoint(subscription, platform, botInstanceId, chatId);
            subscription.EnabledPlatforms = TogglePlatform(subscription.EnabledPlatforms, platformFlag);
            subscription.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task EnableAsync(
        long coreUserId,
        BotPlatform platform,
        string botInstanceId,
        string chatId,
        string notificationType,
        long targetId,
        CancellationToken cancellationToken = default)
    {
        var subscription = await dbContext.NotificationSubscriptions
            .FirstOrDefaultAsync(item => item.CoreUserId == coreUserId
                && item.NotificationType == notificationType
                && item.TargetId == targetId,
                cancellationToken);
        var platformFlag = ToFlag(platform);
        if (platformFlag is NotificationPlatformFlags.None)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (subscription is null)
        {
            subscription = new NotificationSubscription
            {
                CoreUserId = coreUserId,
                NotificationType = notificationType,
                TargetId = targetId,
                EnabledPlatforms = (int)platformFlag,
                CreatedAt = now,
                UpdatedAt = now
            };
            SetEndpoint(subscription, platform, botInstanceId, chatId);
            dbContext.NotificationSubscriptions.Add(subscription);
        }
        else
        {
            SetEndpoint(subscription, platform, botInstanceId, chatId);
            subscription.EnabledPlatforms = SetPlatform(subscription.EnabledPlatforms, platformFlag, enabled: true);
            subscription.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ToggleAllAsync(
        long coreUserId,
        BotPlatform platform,
        string botInstanceId,
        string chatId,
        string notificationType,
        IReadOnlyCollection<long> targetIds,
        CancellationToken cancellationToken = default)
    {
        if (targetIds.Count == 0)
        {
            return;
        }

        var subscriptions = await dbContext.NotificationSubscriptions
            .Where(item => item.CoreUserId == coreUserId
                && item.NotificationType == notificationType
                && targetIds.Contains(item.TargetId))
            .ToListAsync(cancellationToken);
        var platformFlag = ToFlag(platform);
        var byTarget = subscriptions.ToDictionary(item => item.TargetId);
        var shouldEnable = targetIds.Any(id => !byTarget.TryGetValue(id, out var subscription)
            || !HasPlatform(subscription.EnabledPlatforms, platformFlag));
        var now = timeProvider.GetUtcNow();

        foreach (var targetId in targetIds)
        {
            if (byTarget.TryGetValue(targetId, out var subscription))
            {
                SetEndpoint(subscription, platform, botInstanceId, chatId);
                subscription.EnabledPlatforms = SetPlatform(subscription.EnabledPlatforms, platformFlag, shouldEnable);
                subscription.UpdatedAt = now;
                continue;
            }

            var created = new NotificationSubscription
            {
                CoreUserId = coreUserId,
                NotificationType = notificationType,
                TargetId = targetId,
                EnabledPlatforms = (int)(shouldEnable
                    ? NotificationPlatformFlags.All
                    : NotificationPlatformFlags.All & ~platformFlag),
                CreatedAt = now,
                UpdatedAt = now
            };
            SetEndpoint(created, platform, botInstanceId, chatId);
            dbContext.NotificationSubscriptions.Add(created);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static NotificationPlatformFlags ToFlag(BotPlatform platform)
    {
        return platform switch
        {
            BotPlatform.Telegram => NotificationPlatformFlags.Telegram,
            BotPlatform.Qq => NotificationPlatformFlags.QQ,
            _ => NotificationPlatformFlags.None
        };
    }

    private static bool HasPlatform(int value, NotificationPlatformFlags flag)
    {
        return flag is not NotificationPlatformFlags.None && (((NotificationPlatformFlags)value) & flag) != 0;
    }

    private static int TogglePlatform(int value, NotificationPlatformFlags flag)
    {
        return HasPlatform(value, flag)
            ? (int)(((NotificationPlatformFlags)value) & ~flag)
            : (int)(((NotificationPlatformFlags)value) | flag);
    }

    private static int SetPlatform(int value, NotificationPlatformFlags flag, bool enabled)
    {
        return enabled
            ? (int)(((NotificationPlatformFlags)value) | flag)
            : (int)(((NotificationPlatformFlags)value) & ~flag);
    }

    private static void SetEndpoint(
        NotificationSubscription subscription,
        BotPlatform platform,
        string botInstanceId,
        string chatId)
    {
        switch (platform)
        {
            case BotPlatform.Telegram:
                subscription.TelegramBotInstanceId = botInstanceId;
                subscription.TelegramChatId = chatId;
                break;
            case BotPlatform.Qq:
                subscription.QqBotInstanceId = botInstanceId;
                subscription.QqChatId = chatId;
                break;
        }
    }

    private static NotificationEndpoint? ToEndpoint(NotificationSubscription subscription, BotPlatform platform)
    {
        return platform switch
        {
            BotPlatform.Telegram when !string.IsNullOrWhiteSpace(subscription.TelegramBotInstanceId)
                && !string.IsNullOrWhiteSpace(subscription.TelegramChatId)
                => new NotificationEndpoint(subscription.TelegramBotInstanceId, subscription.TelegramChatId),
            BotPlatform.Qq when !string.IsNullOrWhiteSpace(subscription.QqBotInstanceId)
                && !string.IsNullOrWhiteSpace(subscription.QqChatId)
                => new NotificationEndpoint(subscription.QqBotInstanceId, subscription.QqChatId),
            _ => null
        };
    }
}

public sealed record NotificationEndpoint(string BotInstanceId, string ChatId);
