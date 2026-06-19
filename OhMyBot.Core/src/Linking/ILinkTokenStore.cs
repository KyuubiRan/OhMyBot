namespace OhMyBot.Core.Linking;

public interface ILinkTokenStore
{
    Task SetAsync(string token, LinkTokenPayload payload, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task<LinkTokenPayload?> GetAsync(string token, CancellationToken cancellationToken = default);

    Task RemoveAsync(string token, CancellationToken cancellationToken = default);
}
