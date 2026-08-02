namespace OhMyBot.Core.Infrastructure.Messaging;

public interface IRouteChangePublisher
{
    Task PublishRoutesChangedAsync(long version, CancellationToken cancellationToken = default);
}
