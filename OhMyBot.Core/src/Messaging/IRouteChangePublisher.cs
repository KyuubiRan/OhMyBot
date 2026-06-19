namespace OhMyBot.Core.Messaging;

public interface IRouteChangePublisher
{
    Task PublishRoutesChangedAsync(long version, CancellationToken cancellationToken = default);
}
