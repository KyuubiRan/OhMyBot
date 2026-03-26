using OhMyLib.Attributes;
using OhMyLib.Models.Common;

namespace OhMyLib.Repositories;

[Component]
public class BotUserRepo(OhMyDbContext db) : BaseRepo<BotUser>(db)
{
    public ValueTask<BotUser?> FindByIdAsync(long id, CancellationToken cancellationToken = default) => EntitySet.FindAsync([id], cancellationToken);
}