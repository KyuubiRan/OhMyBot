using Microsoft.EntityFrameworkCore;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Models.AiRouter;

namespace OhMyLib.Repositories;

[Component]
public class AiRouterAccountRepo(OhMyDbContext db) : BaseRepo<AiRouterAccount>(db)
{
    public Task<AiRouterAccount?> FindByIdAsync(long id, bool noTracking = false, CancellationToken cancellationToken = default) =>
        (noTracking ? QueryNoTracking : Query).Include(x => x.OwnerBotUser)
                                      .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<AiRouterAccount?> FindByAccountAsync(string account, bool noTracking = false, CancellationToken cancellationToken = default) =>
        (noTracking ? QueryNoTracking : Query).Include(x => x.OwnerBotUser)
                                      .FirstOrDefaultAsync(x => x.Account == account, cancellationToken);

    public Task<List<AiRouterAccount>> ListByOwnerAsync(string ownerId, SoftwareType softwareType, bool noTracking = false,
                                                        CancellationToken cancellationToken = default) =>
        (noTracking ? QueryNoTracking : Query).Include(x => x.OwnerBotUser)
                                      .Where(x => x.OwnerBotUser.OwnerId == ownerId && x.OwnerBotUser.OwnerType == softwareType)
                                      .OrderBy(x => x.Account)
                                      .ToListAsync(cancellationToken);

    public Task<List<AiRouterAccount>> ListEnabledAutoSignAsync(int offset = 0, int limit = 20, CancellationToken cancellationToken = default) =>
        QueryNoTracking.Include(x => x.OwnerBotUser)
                       .Where(x => x.AutoSignEnabled && x.OwnerBotUser.Privilege > UserPrivilege.None)
                       .OrderBy(x => x.Id)
                       .Skip(offset)
                       .Take(limit)
                       .ToListAsync(cancellationToken);
}
