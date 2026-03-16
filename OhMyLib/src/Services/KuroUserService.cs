using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Models.Kuro;
using OhMyLib.Repositories;

namespace OhMyLib.Services;

[Component]
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class KuroUserService(KuroUserRepo repo, BotUserService service)
{
    public async ValueTask<KuroUser?> FindByBbsIdAsync(long bbsId, CancellationToken cancellationToken = default)
    {
        return await repo.EntitySet.FirstOrDefaultAsync(x => x.BbsUserId == bbsId, cancellationToken: cancellationToken);
    }

    public async ValueTask<KuroUser?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return await repo.EntitySet.FirstOrDefaultAsync(x => x.Id == id, cancellationToken: cancellationToken);
    }

    public async ValueTask CreateOrUpdateUserAsync(long fromId, SoftwareType type, long kUid, string? kToken, string? kDevCode, string? kDistinctId,
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

    public async ValueTask<int> SaveAsync(CancellationToken cancellationToken = default)
    {
        return await repo.SaveChangesAsync(cancellationToken);
    }
}