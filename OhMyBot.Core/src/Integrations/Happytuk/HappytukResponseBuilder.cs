using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Commanding.Notifications;
using OhMyBot.Core.Infrastructure.Data.Entities;
using OhMyBot.Core.Integrations.AiRouter;

namespace OhMyBot.Core.Integrations.Happytuk;

public sealed class HappytukResponseBuilder(
    CallbackActionStore callbackStore,
    NotificationSubscriptionService subscriptionService)
{
    public CommandResponse BuildBindResult(CommandContext context, HappytukAccount account) =>
        CommandResponses.Text($"HappyTuk 账号绑定成功：{account.LoginAccount}", context);

    public CommandResponse BuildRedeemResult(CommandContext context, string couponCode, IReadOnlyList<string> results) =>
        CommandResponses.Text($"[HappyTuk-手动兑换]\n兑换码：{couponCode}\n" + string.Join('\n', results), context);

    // With multiple accounts, coupons may be account-specific, so let the user pick one account or
    // "全部兑换". Names are masked in group chats; the mask decision is baked into the callback data
    // because the callback loses the originating chat type.
    public async Task<CommandResponse> BuildRedeemSelectPanelAsync(
        CommandContext context,
        IReadOnlyList<HappytukAccount> accounts,
        string couponCode,
        bool maskNames,
        CancellationToken cancellationToken = default)
    {
        var response = CommandResponses.Text("请选择要兑换的 HappyTuk 账号：", context);
        foreach (var account in accounts)
        {
            response.AddButtonRow(new ResponseButtonRow
            {
                Buttons =
                {
                    new ResponseButton
                    {
                        Text = maskNames ? HappytukRedeemExecutor.MaskAccount(account.LoginAccount) : account.LoginAccount,
                        Payload = await callbackStore.PutAsync(
                            "happytuk-redeem-select",
                            context.Identity.CoreUserId,
                            context.Request.ChatId,
                            context.Request.UserId,
                            new HappytukRedeemSelectCallbackData(account.Id, couponCode, All: false, MaskNames: maskNames),
                            cancellationToken: cancellationToken)
                    }
                }
            });
        }

        response.AddButtonRow(new ResponseButtonRow
        {
            Buttons =
            {
                new ResponseButton
                {
                    Text = "全部兑换",
                    Payload = await callbackStore.PutAsync(
                        "happytuk-redeem-select",
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        new HappytukRedeemSelectCallbackData(0, couponCode, All: true, MaskNames: maskNames),
                        cancellationToken: cancellationToken)
                }
            }
        });
        return response;
    }

    public async Task<CommandResponse> BuildDeletePanelAsync(
        CommandContext context,
        IReadOnlyList<HappytukAccount> accounts,
        CancellationToken cancellationToken = default)
    {
        var response = CommandResponses.Text("请选择要删除的 HappyTuk 账号：", context);
        foreach (var account in accounts)
        {
            response.AddButtonRow(new ResponseButtonRow
            {
                Buttons =
                {
                    new ResponseButton
                    {
                        Text = account.LoginAccount,
                        Payload = await callbackStore.PutAsync(
                            "happytuk-delete-select",
                            context.Identity.CoreUserId,
                            context.Request.ChatId,
                            context.Request.UserId,
                            new HappytukAccountCallbackData(account.Id),
                            cancellationToken: cancellationToken)
                    }
                }
            });
        }

        return response;
    }

    public async Task<CommandResponse> BuildNotifyAccountPanelAsync(
        CommandContext context,
        IReadOnlyList<HappytukAccount> accounts,
        string? editMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var enabled = await subscriptionService.GetEnabledTargetIdsAsync(
            context.Identity.CoreUserId,
            context.Request.Platform,
            NotificationTypes.HappytukAutoRedeem,
            accounts.Select(account => account.Id).ToArray(),
            cancellationToken);
        var response = CommandResponses.Text(
            $"[消息订阅 · {NotificationTypes.HappytukAutoRedeemDisplayName}]\n当前已启用：" +
            (enabled.Count == 0 ? "无" : string.Join('、', accounts.Where(account => enabled.Contains(account.Id)).Select(account => account.LoginAccount))),
            context);
        if (!string.IsNullOrWhiteSpace(editMessageId))
        {
            response.AsTelegramEdit(editMessageId);
        }

        foreach (var account in accounts)
        {
            response.AddButtonRow(new ResponseButtonRow
            {
                Buttons =
                {
                    new ResponseButton
                    {
                        Text = $"{(enabled.Contains(account.Id) ? "[开]" : "[关]")} {account.LoginAccount}",
                        Payload = await callbackStore.PutAsync(
                            "notify-account-toggle",
                            context.Identity.CoreUserId,
                            context.Request.ChatId,
                            context.Request.UserId,
                            new NotifyAccountCallbackData(NotificationTypes.HappytukAutoRedeem, account.Id, ToggleAll: false),
                            cancellationToken: cancellationToken)
                    }
                }
            });
        }

        response.AddButtonRow(new ResponseButtonRow
        {
            Buttons =
            {
                new ResponseButton
                {
                    Text = "全部切换",
                    Payload = await callbackStore.PutAsync(
                        "notify-account-toggle",
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        new NotifyAccountCallbackData(NotificationTypes.HappytukAutoRedeem, 0, ToggleAll: true),
                        cancellationToken: cancellationToken)
                },
                new ResponseButton
                {
                    Text = "返回",
                    Payload = await callbackStore.PutAsync(
                        "notify-back",
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        new NotifyBackCallbackData(),
                        cancellationToken: cancellationToken)
                }
            }
        });
        return response;
    }
}

public sealed record HappytukAccountCallbackData(long AccountId);

public sealed record HappytukRedeemSelectCallbackData(long AccountId, string CouponCode, bool All, bool MaskNames);

public sealed record HappytukDeleteConfirmCallbackData(long AccountId, bool Confirm);
