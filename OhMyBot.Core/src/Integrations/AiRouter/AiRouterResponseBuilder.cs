using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Commanding.Presentation;
using OhMyBot.Core.Infrastructure.Data.Entities;
using OhMyBot.Core.Commanding.Notifications;

namespace OhMyBot.Core.Integrations.AiRouter;

public sealed class AiRouterResponseBuilder(
    CallbackActionStore callbackStore,
    NotificationSubscriptionService subscriptionService,
    TimeProvider timeProvider)
{
    public Task<CommandResponse> BuildAccountListAsync(
        CommandContext context,
        IReadOnlyList<AiRouterAccount> accounts,
        CancellationToken cancellationToken = default)
    {
        string markdown;
        if (accounts.Count == 0)
        {
            markdown = "尚未绑定 AI Router 账号";
        }
        else
        {
            var lines = new List<string> { MarkdownV2.Escape("[AI Router]"), "已绑定账号：" };
            lines.AddRange(accounts.Select(account =>
                $"\\- {MarkdownV2.CodeSpan(account.DisplayName)} \\({MarkdownV2.Code(account.LoginEmail)}\\)：自动签到{MarkdownV2.Escape(account.AutoSignEnabled ? "开启" : "关闭")}"));
            markdown = string.Join('\n', lines);
        }

        var response = CommandResponses.TelegramMarkdown(
            context.Identity,
            markdown,
            replyToMessageId: context.Request.MessageId);
        return Task.FromResult(response);
    }

    public CommandResponse BuildBindResult(CommandContext context, AiRouterBindResult result)
    {
        var markdown = string.Join('\n',
            MarkdownV2.Escape("绑定成功！"),
            $"账号：{MarkdownV2.CodeSpan(result.DisplayName)}",
            $"邮箱：{MarkdownV2.CodeSpan(result.LoginEmail)}");
        return CommandResponses.TelegramMarkdown(
            context.Identity,
            markdown,
            replyToMessageId: context.Request.MessageId);
    }

    public CommandResponse BuildSignResult(
        CommandContext context,
        AiRouterSignResult result,
        bool autoSign = false)
    {
        var title = autoSign ? "[AI Router-自动签到]" : "[AI Router-手动签到]";
        var status = result.Type switch
        {
            AiRouterSignResultType.Success => "签到成功",
            AiRouterSignResultType.AlreadySigned => "今日已签到",
            _ => "签到失败"
        };
        var lines = new List<string>
        {
            MarkdownV2.Escape(title),
            $"账号：{MarkdownV2.CodeSpan(result.DisplayName)}",
            $"邮箱：{MarkdownV2.CodeSpan(result.LoginEmail)}",
            $"结果：{MarkdownV2.Escape(status)}",
            $"说明：{MarkdownV2.Escape(result.Message)}"
        };

        if (result.SignIn is { } signIn)
        {
            lines.Add($"今日奖励：{MarkdownV2.Escape(signIn.TodayReward.ToString("F2"))}");
            lines.Add($"连续签到：{signIn.CurrentStreak} 天");
            lines.Add($"累计奖励：{MarkdownV2.Escape(signIn.TotalReward.ToString("F2"))}");
            lines.Add($"本月签到：{signIn.MonthSignedDays} 天");
        }

        lines.Add($"时间：{MarkdownV2.Escape(timeProvider.GetUtcNow().ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))}");
        return CommandResponses.TelegramMarkdown(
            context.Identity,
            string.Join('\n', lines),
            replyToMessageId: context.Request.MessageId);
    }

    public async Task<CommandResponse> BuildSignSelectionAsync(
        CommandContext context,
        IReadOnlyList<AiRouterAccount> accounts,
        string? editMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var response = CommandResponses.Text("请选择要签到的 AI Router 账号：", context);
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

        if (accounts.Count > 1)
        {
            response.AddButtonRow(new ResponseButtonRow
            {
                Buttons =
                {
                    new ResponseButton
                    {
                        Text = "全部签到",
                        Payload = await callbackStore.PutAsync(
                            "ai-router-signin-all",
                            context.Identity.CoreUserId,
                            context.Request.ChatId,
                            context.Request.UserId,
                            new AiRouterSignAllCallbackData(),
                            cancellationToken: cancellationToken)
                    }
                }
            });
        }

        return response;
    }

    /// <summary>全部账号签到的合并结果（纯文本）。</summary>
    public CommandResponse BuildCombinedSignResult(
        CommandContext context,
        IReadOnlyList<AiRouterSignResult> results,
        string? editMessageId = null)
    {
        var lines = new List<string> { "[AI Router-手动签到 - 全部账号]" };
        foreach (var result in results)
        {
            var status = result.Type switch
            {
                AiRouterSignResultType.Success => "签到成功",
                AiRouterSignResultType.AlreadySigned => "今日已签到",
                _ => "签到失败"
            };
            lines.Add($"- {result.DisplayName}：{status}{(string.IsNullOrWhiteSpace(result.Message) ? string.Empty : "（" + result.Message + "）")}");
        }

        lines.Add("时间：" + timeProvider.GetUtcNow().ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        var response = CommandResponses.Text(string.Join('\n', lines), context);
        if (!string.IsNullOrWhiteSpace(editMessageId))
        {
            response.AsTelegramEdit(editMessageId);
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
            response.AsTelegramEdit(editMessageId);
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
            response.AddButtonRow(new ResponseButtonRow
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
        var markdown = string.Join('\n',
            MarkdownV2.Escape("[消息订阅管理]"),
            MarkdownV2.Escape("当前已启用：") + (enabled.Count > 0
                ? MarkdownV2.CodeSpan(NotificationTypes.AiRouterAutoSignDisplayName)
                : MarkdownV2.Escape("无")));
        var response = CommandResponses.TelegramMarkdown(
            context.Identity,
            markdown,
            replyToMessageId: editMessageId is null ? context.Request.MessageId : null,
            editMessageId: editMessageId);

        response.AddButtonRow(new ResponseButtonRow
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
        var enabledMarks = accounts
            .Where(account => enabled.Contains(account.Id))
            .Select(account => MarkdownV2.CodeSpan(account.DisplayName))
            .ToArray();
        var markdown = string.Join('\n',
            MarkdownV2.Escape($"[消息订阅 · {NotificationTypes.AiRouterAutoSignDisplayName}]"),
            MarkdownV2.Escape("当前已启用：") + (enabledMarks.Length == 0
                ? MarkdownV2.Escape("无")
                : string.Join(MarkdownV2.Escape("、"), enabledMarks)),
            MarkdownV2.Escape("此处为消息订阅管理，仅控制签到结果是否推送；开关自动签到请使用 ") + MarkdownV2.CodeSpan("/ai router autosign"));
        var response = CommandResponses.TelegramMarkdown(
            context.Identity,
            markdown,
            replyToMessageId: editMessageId is null ? context.Request.MessageId : null,
            editMessageId: editMessageId);

        await AddAccountToggleButtonsAsync(
            response,
            context,
            accounts,
            "notify-account-toggle",
            includeAll: true,
            cancellationToken,
            accountText: account => $"{(enabled.Contains(account.Id) ? "[开]" : "[关]")} {account.DisplayName}",
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
                response.AddButtonRow(row);
                row = new ResponseButtonRow();
            }
        }

        if (row.Buttons.Count > 0)
        {
            response.AddButtonRow(row);
        }

        if (includeAll)
        {
            object allCallbackData = actionType == "notify-account-toggle"
                ? new NotifyAccountCallbackData(NotificationTypes.AiRouterAutoSign, 0, ToggleAll: true)
                : new AiRouterAutoSignCallbackData(0, ToggleAll: true);
            response.AddButtonRow(new ResponseButtonRow
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
                response.FirstTelegram().ButtonRows[^1].Buttons.AddRange(extraAllRowButtons);
            }
        }
    }

    private static string BuildAutoSignText(IReadOnlyList<AiRouterAccount> accounts)
    {
        var enabled = accounts.Where(account => account.AutoSignEnabled).Select(account => $"`{account.DisplayName}`").ToArray();
        return "点击下方按钮进行开/关签到功能\n当前已启用: " + (enabled.Length == 0 ? "无" : string.Join(", ", enabled))
            + "\n如需管理签到结果的消息推送，请使用 `/notify`";
    }
}

public sealed record AiRouterAccountCallbackData(long AccountId);

public sealed record AiRouterSignAllCallbackData;

public sealed record AiRouterAutoSignCallbackData(long AccountId, bool ToggleAll = false);

public sealed record NotifyTypeCallbackData(string Type);

public sealed record NotifyAccountCallbackData(string Type, long AccountId, bool ToggleAll = false);

public sealed record NotifyBackCallbackData;
