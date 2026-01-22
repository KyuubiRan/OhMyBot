using OhMyLib.Attributes;
using OhMyLib.Models.Telegram;

namespace OhMyLib.Repositories;

[Component]
public class TelegramUserRepo(OhMyDbContext db) : BaseRepo<TelegramUser>(db)
{
}