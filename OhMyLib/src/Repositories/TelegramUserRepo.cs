using Microsoft.EntityFrameworkCore;
using OhMyLib.Attributes;
using OhMyLib.Models.Telegram;

namespace OhMyLib.Repositories;

[Component]
public class TelegramUserRepo(OhMyDbContext db) : BaseRepo<TelegramUser>(db)
{
    public Task<TelegramUser?> FindByUserIdAsync(long userId, bool noTracking = false, CancellationToken cancellationToken = default) =>
        (noTracking ? QueryNoTracking : Query).FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken: cancellationToken);

    public Task<TelegramUser?> FindByUsernameAsync(string username, bool noTracking = false, CancellationToken cancellationToken = default) =>
        (noTracking ? QueryNoTracking : Query).FirstOrDefaultAsync(x => x.Username == username, cancellationToken: cancellationToken);
}