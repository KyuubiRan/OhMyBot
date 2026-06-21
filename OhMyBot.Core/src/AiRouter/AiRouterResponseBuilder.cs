using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Callbacks;
using OhMyBot.Core.Commands;
using OhMyBot.Core.Data.Entities;
using OhMyBot.Core.Notifications;

namespace OhMyBot.Core.AiRouter;

public sealed class AiRouterResponseBuilder(
    CallbackActionStore callbackStore,
    NotificationSubscriptionService subscriptionService,
    TimeProvider timeProvider)
{
    public async Task<CommandResponse> BuildAccountListAsync(
        CommandContext context,
        IReadOnlyList<AiRouterAccount> accounts,
        CancellationToken cancellationToken = default)
    {
        var response = CommandResponses.Ok(CommandResponseDataKind.AiRouterAccountList, context);
        response.AiRouterAccountList = new AiRouterAccountListData();
        response.AiRouterAccountList.Accounts.AddRange(accounts.Select(account => ToItem(account, notificationEnabled: false)));
        response.Message = accounts.Count == 0
            ? "尚未绑定 AI Router 账号"
            : "[AI Router]\n已绑定账号：\n" + string.Join('\n', accounts.Select(account => $"- {account.DisplayName} ({account.LoginEmail})：自动签到{(account.AutoSignEnabled ? "开启" : "关闭")}"));
        return await Task.FromResult(response);
    }

    public CommandResponse BuildBindResult(CommandContext context, AiRouterBindResult result)
    {
        var response = CommandResponses.Ok(CommandResponseDataKind.AiRouterBindResult, context, "绑定成功！");
        response.AiRouterBindResult = new AiRouterBindResultData
        {
            AccountId = result.Id,
            LoginEmail = result.LoginEmail,
            DisplayName = result.DisplayName
        };
        return response;
    }

    public CommandResponse BuildSignResult(
        CommandContext context,
        AiRouterSignResult result,
        bool autoSign = false)
    {
        var response = CommandResponses.Ok(CommandResponseDataKind.AiRouterSignResult, context);
        response.AiRouterSignResult = ToSignData(result, autoSign, timeProvider.GetUtcNow());
        return response;
    }

    public async Task<CommandResponse> BuildSignSelectionAsync(
        CommandContext context,
        IReadOnlyList<AiRouterAccount> accounts,
        CancellationToken cancellationToken = default)
    {
        var response = CommandResponses.Text("请选择要签到的 AI Router 账号：", context);
        foreach (var account in accounts)
        {
            response.ButtonRows.Add(new ResponseButtonRow
            {
                Buttons =
                {
                    new ResponseButton
                    {
                        Text = account.DisplayName,
                        Payload = await callbackStore.PutAsync(
                            "ai-router-signin-select",
                            context.Identity.CoreUserId,
                            context.Request.ChatId,
                            context.Request.UserId,
                            new AiRouterAccountCallbackData(account.Id),
                            cancellationToken: cancellationToken)
                    }
                }
            });
        }

        return response;
    }

    public async Task<CommandResponse> BuildAutoSignPanelAsync(
        CommandContext context,
        IReadOnlyList<AiRouterAccount> accounts,
        string? editMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var response = CommandResponses.Text(BuildAutoSignText(accounts), context);
        if (!string.IsNullOrWhiteSpace(editMessageId))
        {
            response.EditMessageId = editMessageId;
            response.ReplyToMessageId = string.Empty;
        }

        await AddAccountToggleButtonsAsync(
            response,
            context,
            accounts,
            "ai-router-auto-sign-toggle",
            includeAll: true,
            cancellationToken);
        return response;
    }

    public async Task<CommandResponse> BuildDeletePanelAsync(
        CommandContext context,
        IReadOnlyList<AiRouterAccount> accounts,
        CancellationToken cancellationToken = default)
    {
        var response = CommandResponses.Text("请选择要删除的 AI Router 账号：", context);
        foreach (var account in accounts)
        {
            response.ButtonRows.Add(new ResponseButtonRow
            {
                Buttons =
                {
                    new ResponseButton
                    {
                        Text = account.DisplayName,
                        Payload = await callbackStore.PutAsync(
                            "ai-router-delete-select",
                            context.Identity.CoreUserId,
                            context.Request.ChatId,
                            context.Request.UserId,
                            new AiRouterAccountCallbackData(account.Id),
                            cancellationToken: cancellationToken)
                    }
                }
            });
        }

        return response;
    }

    public async Task<CommandResponse> BuildNotifyTypePanelAsync(
        CommandContext context,
        IReadOnlyList<AiRouterAccount> accounts,
        string? editMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var enabled = await subscriptionService.GetEnabledTargetIdsAsync(
            context.Identity.CoreUserId,
            context.Request.Platform,
            NotificationTypes.AiRouterAutoSign,
            accounts.Select(account => account.Id).ToArray(),
            cancellationToken);
        var response = CommandResponses.Ok(CommandResponseDataKind.NotifyTypePanel, context);
        response.NotifyTypePanel = new NotifyTypePanelData();
        response.NotifyTypePanel.Items.Add(new NotifyTypeItem
        {
            Type = NotificationTypes.AiRouterAutoSign,
            DisplayName = NotificationTypes.AiRouterAutoSignDisplayName,
            Enabled = enabled.Count > 0
        });
        response.Message = "[消息订阅管理]\n当前已启用: " + (enabled.Count > 0 ? $"`{NotificationTypes.AiRouterAutoSignDisplayName}`" : "无");
        if (!string.IsNullOrWhiteSpace(editMessageId))
        {
            response.EditMessageId = editMessageId;
            response.ReplyToMessageId = string.Empty;
        }

        response.ButtonRows.Add(new ResponseButtonRow
        {
            Buttons =
            {
                new ResponseButton
                {
                    Text = NotificationTypes.AiRouterAutoSignDisplayName,
                    Payload = await callbackStore.PutAsync(
                        "notify-type-select",
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        new NotifyTypeCallbackData(NotificationTypes.AiRouterAutoSign),
                        cancellationToken: cancellationToken)
                }
            }
        });
        return response;
    }

    public async Task<CommandResponse> BuildNotifyAccountPanelAsync(
        CommandContext context,
        IReadOnlyList<AiRouterAccount> accounts,
        string? editMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var enabled = await subscriptionService.GetEnabledTargetIdsAsync(
            context.Identity.CoreUserId,
            context.Request.Platform,
            NotificationTypes.AiRouterAutoSign,
            accounts.Select(account => account.Id).ToArray(),
            cancellationToken);
        var response = CommandResponses.Ok(CommandResponseDataKind.NotifyAccountPanel, context);
        response.NotifyAccountPanel = new NotifyAccountPanelData
        {
            Type = NotificationTypes.AiRouterAutoSign,
            DisplayName = NotificationTypes.AiRouterAutoSignDisplayName
        };
        response.NotifyAccountPanel.Accounts.AddRange(accounts.Select(account => ToItem(account, enabled.Contains(account.Id))));
        response.Message = $"[{NotificationTypes.AiRouterAutoSignDisplayName}]\n当前已启用: " + FormatEnabledAccounts(accounts, enabled);
        if (!string.IsNullOrWhiteSpace(editMessageId))
        {
            response.EditMessageId = editMessageId;
            response.ReplyToMessageId = string.Empty;
        }

        await AddAccountToggleButtonsAsync(
            response,
            context,
            accounts,
            "notify-account-toggle",
            includeAll: true,
            cancellationToken,
            accountText: account => account.DisplayName,
            dataFactory: account => new NotifyAccountCallbackData(NotificationTypes.AiRouterAutoSign, account.Id, ToggleAll: false),
            extraAllRowButtons:
            [
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
            ]);
        return response;
    }

    public static AiRouterSignResultData ToSignData(
        AiRouterSignResult result,
        bool autoSign,
        DateTimeOffset occurredAt)
    {
        return new AiRouterSignResultData
        {
            LoginEmail = result.LoginEmail,
            DisplayName = result.DisplayName,
            Status = result.Type switch
            {
                AiRouterSignResultType.Success => "success",
                AiRouterSignResultType.AlreadySigned => "already_signed",
                _ => "failed"
            },
            Message = result.Message,
            TodayReward = result.SignIn?.TodayReward.ToString("F2") ?? string.Empty,
            CurrentStreak = result.SignIn?.CurrentStreak ?? 0,
            TotalReward = result.SignIn?.TotalReward.ToString("F2") ?? string.Empty,
            MonthSignedDays = result.SignIn?.MonthSignedDays ?? 0,
            TokenRefreshed = result.TokenRefreshed,
            OccurredAtUnixSeconds = occurredAt.ToUnixTimeSeconds(),
            AutoSign = autoSign
        };
    }

    private async Task AddAccountToggleButtonsAsync(
        CommandResponse response,
        CommandContext context,
        IReadOnlyList<AiRouterAccount> accounts,
        string actionType,
        bool includeAll,
        CancellationToken cancellationToken,
        Func<AiRouterAccount, string>? accountText = null,
        Func<AiRouterAccount, object>? dataFactory = null,
        IReadOnlyList<ResponseButton>? extraAllRowButtons = null)
    {
        var row = new ResponseButtonRow();
        foreach (var account in accounts)
        {
            row.Buttons.Add(new ResponseButton
            {
                Text = accountText?.Invoke(account) ?? $"{(account.AutoSignEnabled ? "[开]" : "[关]")} {account.DisplayName}",
                Payload = await callbackStore.PutAsync(
                    actionType,
                    context.Identity.CoreUserId,
                    context.Request.ChatId,
                    context.Request.UserId,
                    dataFactory is null ? new AiRouterAccountCallbackData(account.Id) : dataFactory(account)!,
                    cancellationToken: cancellationToken)
            });

            if (row.Buttons.Count == 2)
            {
                response.ButtonRows.Add(row);
                row = new ResponseButtonRow();
            }
        }

        if (row.Buttons.Count > 0)
        {
            response.ButtonRows.Add(row);
        }

        if (includeAll)
        {
            object allCallbackData = actionType == "notify-account-toggle"
                ? new NotifyAccountCallbackData(NotificationTypes.AiRouterAutoSign, 0, ToggleAll: true)
                : new AiRouterAutoSignCallbackData(0, ToggleAll: true);
            response.ButtonRows.Add(new ResponseButtonRow
            {
                Buttons =
                {
                    new ResponseButton
                    {
                        Text = "开启/关闭全部",
                        Payload = await callbackStore.PutAsync(
                            actionType,
                            context.Identity.CoreUserId,
                            context.Request.ChatId,
                            context.Request.UserId,
                            allCallbackData,
                            cancellationToken: cancellationToken)
                    }
                }
            });
            if (extraAllRowButtons is { Count: > 0 })
            {
                response.ButtonRows[^1].Buttons.AddRange(extraAllRowButtons);
            }
        }
    }

    private static string BuildAutoSignText(IReadOnlyList<AiRouterAccount> accounts)
    {
        var enabled = accounts.Where(account => account.AutoSignEnabled).Select(account => $"`{account.DisplayName}`").ToArray();
        return "点击下方按钮进行开/关签到功能\n当前已启用: " + (enabled.Length == 0 ? "无" : string.Join(", ", enabled));
    }

    private static string FormatEnabledAccounts(IReadOnlyList<AiRouterAccount> accounts, HashSet<long> enabled)
    {
        var names = accounts
            .Where(account => enabled.Contains(account.Id))
            .Select(account => $"`{account.DisplayName}`")
            .ToArray();
        return names.Length == 0 ? "无" : string.Join(", ", names);
    }

    private static AiRouterAccountItem ToItem(AiRouterAccount account, bool notificationEnabled)
    {
        return new AiRouterAccountItem
        {
            Id = account.Id,
            LoginEmail = account.LoginEmail,
            DisplayName = account.DisplayName,
            AutoSignEnabled = account.AutoSignEnabled,
            NotificationEnabled = notificationEnabled
        };
    }
}

public sealed record AiRouterAccountCallbackData(long AccountId);

public sealed record AiRouterAutoSignCallbackData(long AccountId, bool ToggleAll = false);

public sealed record NotifyTypeCallbackData(string Type);

public sealed record NotifyAccountCallbackData(string Type, long AccountId, bool ToggleAll = false);

public sealed record NotifyBackCallbackData;
