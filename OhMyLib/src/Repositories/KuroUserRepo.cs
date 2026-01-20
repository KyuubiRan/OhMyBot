using OhMyLib.Attributes;
using OhMyLib.Models.Kuro;

namespace OhMyLib.Repositories;

[Component]
public class KuroUserRepo(OhMyDbContext db) : BaseRepo<KuroUser>(db)
{
}