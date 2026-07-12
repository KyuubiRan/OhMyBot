using OhMyBot.Core.Infrastructure.Data.Entities;

namespace OhMyBot.Core.Integrations.Happytuk;

public sealed class HappytukRedeemService(
    HappytukAccountService accountService,
    HappytukHttpClient client,
    HappytukBrowserClient browserClient)
{
    public async Task<HappytukRedeemResult> RedeemAsync(
        HappytukAccount account,
        string couponCode,
        CancellationToken cancellationToken = default)
    {
        couponCode = couponCode.Trim();
        if (string.IsNullOrWhiteSpace(couponCode))
        {
            throw new InvalidOperationException("兑换码不能为空");
        }

        var session = await accountService.GetSessionAsync(account.Id, cancellationToken)
                      ?? await accountService.RefreshSessionAsync(account, cancellationToken);
        HappytukRedeemResultType type;
        try
        {
            type = await client.RedeemAsync(session, couponCode, cancellationToken);
        }
        catch (HappytukBrowserRequiredException)
        {
            type = await RedeemInBrowserAsync(account, couponCode, cancellationToken);
        }

        if (type == HappytukRedeemResultType.SessionExpired)
        {
            session = await accountService.RefreshSessionAsync(account, cancellationToken);
            try
            {
                type = await client.RedeemAsync(session, couponCode, cancellationToken);
            }
            catch (HappytukBrowserRequiredException)
            {
                type = await RedeemInBrowserAsync(account, couponCode, cancellationToken);
            }
        }

        var message = type switch
        {
            HappytukRedeemResultType.Success => "兑换成功",
            HappytukRedeemResultType.AlreadyRedeemed => "该兑换码已兑换",
            HappytukRedeemResultType.SessionExpired => "登录会话已失效",
            _ => "兑换失败"
        };
        return new HappytukRedeemResult(account.Id, account.LoginAccount, couponCode, type, message);
    }

    // .NET HttpClient's TLS fingerprint is CF-challenged on the redeem endpoints even with a valid
    // clearance cookie, so run the whole redeem inside the real browser instead.
    private Task<HappytukRedeemResultType> RedeemInBrowserAsync(
        HappytukAccount account,
        string couponCode,
        CancellationToken cancellationToken) =>
        browserClient.RedeemAsync(
            account.LoginAccount,
            accountService.DecryptPassword(account),
            couponCode,
            cancellationToken);
}
