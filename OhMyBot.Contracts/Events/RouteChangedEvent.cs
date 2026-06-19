namespace OhMyBot.Contracts.Events;

public sealed record RouteChangedEvent(
    string Type,
    long Version,
    DateTimeOffset ChangedAt)
{
    public const string EventType = "routes.changed";

    public static RouteChangedEvent Create(long version, DateTimeOffset changedAt)
    {
        return new RouteChangedEvent(EventType, version, changedAt);
    }
}
