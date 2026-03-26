using Microsoft.EntityFrameworkCore;
using OhMyLib.Attributes;
using OhMyLib.Models.Kuro;

namespace OhMyLib.Repositories;

[Component]
public class KuroUserRepo(OhMyDbContext db) : BaseRepo<KuroUser>(db)
{
    public Task<KuroUser?> FindByBbsIdAsync(long bbsId, bool noTracking = false, CancellationToken cancellationToken = default) =>
        (noTracking ? QueryNoTracking : Query).FirstOrDefaultAsync(x => x.BbsUserId == bbsId, cancellationToken: cancellationToken);

    public Task<KuroUser?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        Query.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
}