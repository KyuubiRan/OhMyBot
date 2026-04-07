using OhMyLib.Attributes;
using OhMyLib.Enums;
using OhMyLib.Enums.Kuro;
using OhMyLib.Models.Kuro;
using OhMyLib.Requests.Kuro;

namespace OhMyLib.Services.Kuro;

[Component]
public class KuroAccountService(BotUserService botUserService, KuroUserService kuroUserService)
{
    public async Task<KuroBindPreviewResult> GetBindPreviewAsync(
        string ownerId,
        SoftwareType softwareType,
        KuroBindPayload payload,
        CancellationToken cancellationToken = default)
    {
        var owner = await botUserService.GetUserAsync(ownerId, softwareType, cancellationToken)
                    ?? throw new InvalidOperationException("Owner bot user not found.");

        var existsBinding = await kuroUserService.FindByBbsIdAsync(payload.KuroUserId, cancellationToken);
        if (existsBinding != null && existsBinding.OwnerUserId != owner.Id)
            throw new InvalidOperationException("该库街区UID已被绑定，如有疑问请联系Bot管理员处理");

        using var client = new KuroHttpClient(payload.Token, payload.DevCode, payload.DistinctId, payload.IpAddress);
        var me = await client.BbsGetMineAsync(payload.KuroUserId);
        if (!me.Success)
            throw new InvalidOperationException($"获取库街区信息失败({me.Code})：{me.Msg}");

        var mine = me.Data?.Mine ?? throw new InvalidOperationException("获取库街区信息失败：返回数据为空");
        return new KuroBindPreviewResult(mine.UserId ?? payload.KuroUserId.ToString(), mine.UserName ?? string.Empty);
    }

    public async Task BindAsync(long fromId, SoftwareType softwareType, KuroBindPayload payload, CancellationToken cancellationToken = default)
    {
        await kuroUserService.CreateOrUpdateUserAsync(
            fromId,
            softwareType,
            payload.KuroUserId,
            payload.Token,
            payload.DevCode,
            payload.DistinctId,
            payload.IpAddress,
            cancellationToken);
    }

    public async Task<KuroInitCharactersResult> InitializeGameCharactersAsync(
        string ownerId,
        SoftwareType softwareType,
        CancellationToken cancellationToken = default)
    {
        var snapshotUser = await botUserService.GetUserWithKuroAsync(ownerId, softwareType, noTracking: true, cancellationToken)
                           ?? throw new InvalidOperationException("请先绑定库街区账号后再使用初始化游戏角色功能");

        var snapshotKuroUser = snapshotUser.KuroUser;
        if (snapshotKuroUser == null || string.IsNullOrWhiteSpace(snapshotKuroUser.Token) || snapshotKuroUser.BbsUserId == null)
            throw new InvalidOperationException("请先绑定库街区账号后再使用初始化游戏角色功能");

        using var client = new KuroHttpClient(snapshotKuroUser);
        var defaults = await client.BbsGetDefaultRoleAsync(snapshotKuroUser.BbsUserId.Value);

        if (defaults.Code == 220)
        {
            await kuroUserService.InvalidateAsync(snapshotKuroUser.Id, cancellationToken);
            throw new InvalidOperationException("Token已失效，请重新绑定库街区账号后再使用签到功能");
        }

        if (!defaults.Success || defaults.Data == null)
            throw new InvalidOperationException("获取默认角色信息失败：" + defaults.Msg);

        if (defaults.Data.DefaultRoleList.Count == 0)
            return new KuroInitCharactersResult([]);

        var trackedUser = await botUserService.GetUserWithKuroAsync(ownerId, softwareType, cancellationToken: cancellationToken)
                          ?? throw new InvalidOperationException("请先绑定库街区账号后再使用初始化游戏角色功能");

        var trackedKuroUser = trackedUser.KuroUser
                              ?? throw new InvalidOperationException("请先绑定库街区账号后再使用初始化游戏角色功能");

        var resultItems = new List<KuroInitializedCharacter>();

        foreach (var role in defaults.Data.DefaultRoleList)
        {
            var has = trackedKuroUser.GameConfigs.FirstOrDefault(x => (int)x.GameType == role.GameId);
            if (has == null)
            {
                has = new KuroGameConfig
                {
                    KuroUser = trackedKuroUser,
                    GameType = (KuroGameType)role.GameId,
                    GameCharacterUid = long.Parse(role.RoleId)
                };

                trackedKuroUser.GameConfigs.Add(has);
            }
            else
            {
                has.GameCharacterUid = long.Parse(role.RoleId);
            }

            resultItems.Add(new KuroInitializedCharacter(role.ServerName, role.RoleId, role.RoleName, role.GameLevel, role.ActiveDay));
        }

        await botUserService.SaveAsync(cancellationToken);
        return new KuroInitCharactersResult(resultItems);
    }
}

public sealed record KuroBindPayload(
    long KuroUserId,
    string Token,
    string? DevCode = null,
    string? DistinctId = null,
    string? IpAddress = null);

public sealed record KuroBindPreviewResult(string UserId, string UserName);

public sealed record KuroInitializedCharacter(string ServerName, string RoleId, string RoleName, string GameLevel, int ActiveDay);

public sealed record KuroInitCharactersResult(IReadOnlyList<KuroInitializedCharacter> Characters)
{
    public bool IsEmpty => Characters.Count == 0;
}