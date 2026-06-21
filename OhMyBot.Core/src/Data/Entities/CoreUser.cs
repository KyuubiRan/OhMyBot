using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Data.Entities;

public class CoreUser
{
    public long Id { get; set; }

    public UserPrivilege Privilege { get; set; } = UserPrivilege.User;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<PlatformIdentity> Identities { get; set; } = new List<PlatformIdentity>();

    public ICollection<AiRouterAccount> AiRouterAccounts { get; set; } = new List<AiRouterAccount>();

    public ICollection<NotificationSubscription> NotificationSubscriptions { get; set; } = new List<NotificationSubscription>();
}
