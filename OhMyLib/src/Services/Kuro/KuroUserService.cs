using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Models.Kuro;
using OhMyLib.Repositories;

namespace OhMyLib.Services.Kuro;

[Component]
public class KuroUserService(KuroUserRepo repo, BotUserService service)
{
    public async Task<KuroUser?> FindByBbsIdAsync(long bbsId, CancellationToken cancellationToken = default) =>
        await repo.FindByBbsIdAsync(bbsId, cancellationToken: cancellationToken);

    public async Task<KuroUser?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        await repo.FindByIdAsync(id, cancellationToken: cancellationToken);

    public async Task CreateOrUpdateUserAsync(long fromId, SoftwareType type, long kUid, string? kToken, string? kDevCode, string? kDistinctId,
                                              string? ipAddress, CancellationToken cancellationToken = default)
    {
        var user = await service.GetUserAsync(fromId.ToString(), type, cancellationToken);
        if (user == null)
            throw new InvalidOperationException("Owner bot user not found.");

        var exists = user.KuroUser;

        if (exists != null)
        {
            exists.BbsUserId = kUid;
            exists.Token = kToken;
            exists.DevCode = kDevCode;
            exists.DistinctId = kDistinctId;
            exists.IpAddress = ipAddress;
        }
        else
        {
            var newUser = new KuroUser
            {
                OwnerBotUser = user,
                OwnerUserId = user.Id,
                BbsUserId = kUid,
                Token = kToken,
                DevCode = kDevCode,
                DistinctId = kDistinctId,
                IpAddress = ipAddress
            };

            await repo.AddAsync(newUser, cancellationToken);
        }

        await repo.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> SaveAsync(CancellationToken cancellationToken = default)
    {
        return await repo.SaveChangesAsync(cancellationToken);
    }

    public async Task InvalidateAsync(long kuroUserId, CancellationToken cancellationToken = default)
    {
        var user = await repo.FindByIdAsync(kuroUserId, cancellationToken);
        if (user == null)
            return;

        user.Invalidate();
        await repo.SaveChangesAsync(cancellationToken);
    }
}