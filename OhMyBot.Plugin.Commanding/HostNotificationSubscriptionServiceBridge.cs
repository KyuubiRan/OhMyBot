using Microsoft.Extensions.DependencyInjection;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Notifications;
using OhMyBot.Plugin.Abstractions;

namespace OhMyBot.Plugin.Commanding;

/// <summary>
/// Resolves the Core-owned notification store inside a fresh host scope for each operation.
/// Plugin DbContexts therefore never map Core notification tables.
/// </summary>
public sealed class HostNotificationSubscriptionServiceBridge(IPluginHostServices hostServices)
    : INotificationSubscriptionService
{
    public Task<HashSet<long>> GetEnabledTargetIdsAsync(long coreUserId, BotPlatform platform,
        string notificationType, IReadOnlyCollection<long> knownTargetIds, CancellationToken cancellationToken = default)
        => InvokeAsync(service => service.GetEnabledTargetIdsAsync(
            coreUserId, platform, notificationType, knownTargetIds, cancellationToken));

    public Task<List<NotificationDelivery>> ListEnabledDeliveriesByTargetAsync(string notificationType,
        long targetId, CancellationToken cancellationToken = default)
        => InvokeAsync(service => service.ListEnabledDeliveriesByTargetAsync(
            notificationType, targetId, cancellationToken));

    public Task ToggleAsync(long coreUserId, BotPlatform platform, string botInstanceId, string chatId,
        string notificationType, long targetId, CancellationToken cancellationToken = default)
        => InvokeAsync(service => service.ToggleAsync(coreUserId, platform, botInstanceId, chatId,
            notificationType, targetId, cancellationToken));

    public Task EnableAsync(long coreUserId, BotPlatform platform, string botInstanceId, string chatId,
        string notificationType, long targetId, CancellationToken cancellationToken = default)
        => InvokeAsync(service => service.EnableAsync(coreUserId, platform, botInstanceId, chatId,
            notificationType, targetId, cancellationToken));

    public Task ToggleAllAsync(long coreUserId, BotPlatform platform, string botInstanceId, string chatId,
        string notificationType, IReadOnlyCollection<long> targetIds, CancellationToken cancellationToken = default)
        => InvokeAsync(service => service.ToggleAllAsync(coreUserId, platform, botInstanceId, chatId,
            notificationType, targetIds, cancellationToken));

    public Task DeleteTargetAsync(long coreUserId, string notificationType, long targetId,
        CancellationToken cancellationToken = default)
        => InvokeAsync(service => service.DeleteTargetAsync(
            coreUserId, notificationType, targetId, cancellationToken));

    private async Task<T> InvokeAsync<T>(Func<INotificationSubscriptionService, Task<T>> action)
    {
        await using var scope = hostServices.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<INotificationSubscriptionService>());
    }

    private async Task InvokeAsync(Func<INotificationSubscriptionService, Task> action)
    {
        await using var scope = hostServices.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<INotificationSubscriptionService>());
    }
}
