using System.Collections.Concurrent;
using OhMyBot.Core.Infrastructure.Data.Entities;

namespace OhMyBot.Core.Integrations.Happytuk;

// Shared redeem execution for both the /happytuk redeem command and its account-selection callback:
// runs the coupon against the given accounts under a per-user in-flight lock (anti-spam dedup) and
// returns the per-account result lines. Returns null when a redeem for the user is already running.
internal static class HappytukRedeemExecutor
{
    private static readonly ConcurrentDictionary<long, byte> InProgress = new();

    public static async Task<List<string>?> TryRunAsync(
        long coreUserId,
        HappytukRedeemService redeemService,
        IReadOnlyList<HappytukAccount> accounts,
        string couponCode,
        bool maskNames,
        CancellationToken cancellationToken)
    {
        if (!InProgress.TryAdd(coreUserId, 0))
        {
            return null;
        }

        try
        {
            var results = new List<string>();
            foreach (var account in accounts)
            {
                var name = maskNames ? MaskAccount(account.LoginAccount) : account.LoginAccount;
                try
                {
                    var result = await redeemService.RedeemAsync(account, couponCode, cancellationToken);
                    results.Add($"{name}：{result.Message}");
                }
                catch (Exception exception)
                {
                    results.Add($"{name}：兑换失败（{exception.GetBaseException().Message}）");
                }
            }

            return results;
        }
        finally
        {
            InProgress.TryRemove(coreUserId, out _);
        }
    }

    // Masks an account for group chats: keeps the head and last character, e.g. "account" -> "ac***t",
    // shorter ones -> "a***t". Full account is only ever shown in private chats.
    public static string MaskAccount(string account)
    {
        if (account.Length <= 1)
        {
            return account.Length == 0 ? account : account + "***";
        }

        var head = account.Length >= 6 ? 2 : 1;
        return string.Concat(account.AsSpan(0, head), "***", account.AsSpan(account.Length - 1));
    }
}
