using OhMyLib.Attributes;
using OhMyLib.Models.Common;

namespace OhMyLib.Repositories;

[Component]
public class BotUserRepo(OhMyDbContext db) : BaseRepo<BotUser>(db)
{
}