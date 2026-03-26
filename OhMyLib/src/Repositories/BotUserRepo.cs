using Microsoft.EntityFrameworkCore;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Models.Common;

namespace OhMyLib.Repositories;

[Component]
public class BotUserRepo(OhMyDbContext db) : BaseRepo<BotUser>(db)
{
    public ValueTask<BotUser?> FindByIdAsync(long id, CancellationToken cancellationToken = default) => EntitySet.FindAsync([id], cancellationToken);

    public Task<BotUser?> FindByOwnerIdAndSoftwareAsync(
        string id, SoftwareType softwareType, bool noTracking = false,
        CancellationToken cancellationToken = default)
        => (noTracking ? QueryNoTracking : Query).FirstOrDefaultAsync(x => x.OwnerId == id && x.OwnerType == softwareType, cancellationToken);

    public Task<Dictionary<long, string>> QueryIdAndOwnerIdBySoftwareAsync(
        SoftwareType softwareType, int offset = 0, int limit = 20,
        CancellationToken cancellationToken = default)
        => QueryNoTracking.Where(x => x.OwnerType == softwareType && x.Privilege > UserPrivilege.None)
                          .Where(x => x.KuroUser != null && x.KuroUser.Token != null)
                          .Skip(offset)
                          .Take(limit)
                          .ToDictionaryAsync(x => x.Id, x => x.OwnerId, cancellationToken: cancellationToken);
}