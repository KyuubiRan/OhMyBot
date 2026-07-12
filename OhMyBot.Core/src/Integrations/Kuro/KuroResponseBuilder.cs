using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Integrations.AiRouter;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Commanding.Presentation;
using OhMyBot.Core.Infrastructure.Data.Entities;
using OhMyBot.Core.Commanding.Notifications;

namespace OhMyBot.Core.Integrations.Kuro;

public sealed class KuroResponseBuilder(
    CallbackActionStore callbackStore,
    NotificationSubscriptionService subscriptionService,
    TimeProvider timeProvider)
{
    private const int AccountsPerPage = 8;
    private const int RolesPerPage = 6;

    public async Task<CommandResponse> BuildAccountListAsync(
        CommandContext context,
        IReadOnlyList<KuroAccount> accounts,
        CancellationToken cancellationToken = default)
    {
        var markdown = RenderAccountListMarkdown(accounts);
        var response = CommandResponses.TelegramMarkdown(
            context.Identity, markdown, replyToMessageId: context.Request.MessageId);
        return await Task.FromResult(response);
    }

    public CommandResponse BuildBindResult(CommandContext context, KuroBindResult result)
    {
        var markdown = RenderBindResultMarkdown(result);
        return CommandResponses.TelegramMarkdown(
            context.Identity, markdown, replyToMessageId: context.Request.MessageId);
    }

    public CommandResponse BuildBbsSignResult(
        CommandContext context,
        KuroBbsSignResult result,
        bool autoSign = false)
    {
        var title = autoSign ? "[库街区-自动社区签到]" : "[库街区-手动社区签到]";
        var markdown = RenderSignResultMarkdown(title, result.Account, result.Lines);
        return CommandResponses.TelegramMarkdown(
            context.Identity, markdown, replyToMessageId: context.Request.MessageId);
    }

    public CommandResponse BuildGameSignResult(
        CommandContext context,
        KuroGameSignResult result,
        bool autoSign = false)
    {
        var title = autoSign ? "[库街区-自动游戏签到]" : "[库街区-手动游戏签到]";
        var markdown = RenderSignResultMarkdown(title, result.Account, result.Lines);
        return CommandResponses.TelegramMarkdown(
            context.Identity, markdown, replyToMessageId: context.Request.MessageId);
    }

    public async Task<CommandResponse> BuildBbsSignSelectionAsync(
        CommandContext context,
        IReadOnlyList<KuroAccount> accounts,
        IReadOnlyList<string> actions,
        string? editMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var response = CommandResponses.Text("请选择要执行社区签到的库街区账号：", context);
        ApplyEdit(response, editMessageId);
        foreach (var account in accounts)
        {
            response.AddButtonRow(SingleButtonRow(await ButtonAsync(
                context, "kuro-bbs-sign-select", $"{account.DisplayName} #{account.Id}",
                new KuroBbsSignCallbackData(account.Id, actions.ToArray()), cancellationToken)));
        }

        if (accounts.Count > 1)
        {
            response.AddButtonRow(SingleButtonRow(await ButtonAsync(
                context, "kuro-bbs-sign-all", "全部签到", new KuroBbsSignAllCallbackData(), cancellationToken)));
        }

        return response;
    }

    public async Task<CommandResponse> BuildGameSignSelectionAsync(
        CommandContext context,
        IReadOnlyList<KuroAccount> accounts,
        string? editMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var response = CommandResponses.Text("请选择要执行游戏签到的库街区账号：", context);
        ApplyEdit(response, editMessageId);
        foreach (var account in accounts)
        {
            response.AddButtonRow(SingleButtonRow(await ButtonAsync(
                context, "kuro-game-sign-panel", $"{account.DisplayName} #{account.Id}",
                new KuroGameSignPanelCallbackData(account.Id), cancellationToken)));
        }

        if (accounts.Count > 1)
        {
            response.AddButtonRow(SingleButtonRow(await ButtonAsync(
                context, "kuro-game-sign-all", "全部签到", new KuroGameSignAllCallbackData(), cancellationToken)));
        }

        return response;
    }

    /// <summary>
    /// 游戏签到勾选面板：每个游戏一个开关按钮（√ 签到 / × 跳过），底部为「签到」「返回」。
    /// </summary>
    public async Task<CommandResponse> BuildGameSignPanelAsync(
        CommandContext context,
        KuroAccount account,
        IReadOnlyCollection<long> selected,
        string? editMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var available = AvailableGameIds(account);
        if (available.Count == 0)
        {
            var empty = CommandResponses.Text(
                $"账号 #{account.Id} {account.DisplayName} 暂无游戏角色，请先使用 /kuro game init {account.Id} 同步", context);
            ApplyEdit(empty, editMessageId);
            return empty;
        }

        var selectedSet = new HashSet<long>(selected);
        var lines = new List<string>
        {
            "[库街区游戏签到]",
            $"账号：#{account.Id} {account.DisplayName}",
            "勾选要签到的游戏（√=签到 ×=跳过），然后点击「签到」："
        };
        lines.AddRange(available.Select(gameId => FormatGameRolesLine(account, gameId)));
        var response = CommandResponses.Text(string.Join('\n', lines), context);
        ApplyEdit(response, editMessageId);

        foreach (var gameId in available)
        {
            var name = KuroGameNames.Format(gameId, account.Roles.FirstOrDefault(role => role.GameId == gameId)?.GameName ?? string.Empty);
            response.AddButtonRow(SingleButtonRow(await ButtonAsync(
                context, "kuro-game-sign-panel", $"{(selectedSet.Contains(gameId) ? "[√]" : "[×]")} {name}",
                new KuroGameSignPanelCallbackData(account.Id, Toggle: gameId), cancellationToken)));
        }

        response.AddButtonRow(new ResponseButtonRow
        {
            Buttons =
            {
                await ButtonAsync(context, "kuro-game-sign-run", "签到",
                    new KuroGameSignPanelCallbackData(account.Id), cancellationToken),
                await ButtonAsync(context, "kuro-game-sign-back", "返回",
                    new KuroGameSignBackCallbackData(), cancellationToken)
            }
        });
        return response;
    }

    /// <summary>全部账号签到的合并结果（纯文本）。</summary>
    public CommandResponse BuildCombinedResult(
        CommandContext context,
        string title,
        IReadOnlyList<(KuroAccount Account, IReadOnlyList<string> Lines)> results,
        string? editMessageId = null)
    {
        var blocks = new List<string> { title };
        foreach (var (account, lines) in results)
        {
            blocks.Add(string.Empty);
            blocks.Add($"#{account.Id} {account.DisplayName}");
            blocks.AddRange(lines.Count == 0 ? ["无结果"] : lines);
        }

        blocks.Add(string.Empty);
        blocks.Add("时间：" + timeProvider.GetUtcNow().ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        var response = CommandResponses.Text(string.Join('\n', blocks), context);
        ApplyEdit(response, editMessageId);
        return response;
    }

    /// <summary>账号已同步角色去重后的游戏 Id，按 Id 排序。</summary>
    public static IReadOnlyList<long> AvailableGameIds(KuroAccount account)
    {
        return account.Roles.Select(role => role.GameId).Distinct().OrderBy(id => id).ToArray();
    }

    /// <summary>“显式清空（无勾选）”的存储标记，用于与“未设置（默认全选）”区分。</summary>
    public const string NoneSelectionSentinel = "-";

    /// <summary>面板初始勾选：账号上次持久化的选择（与当前可用游戏取交集）；未设置时默认全选，显式清空则为空。</summary>
    public static IReadOnlyList<long> ResolveGameSignSelection(KuroAccount account)
    {
        var available = AvailableGameIds(account);
        if (string.IsNullOrEmpty(account.GameSignSelection))
        {
            return available;
        }

        if (account.GameSignSelection == NoneSelectionSentinel)
        {
            return [];
        }

        var stored = account.GameSignSelection
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => long.TryParse(part, out var id) ? id : 0)
            .Where(id => id != 0)
            .ToHashSet();
        return available.Where(id => stored.Contains(id)).ToArray();
    }

    /// <summary>把勾选集合序列化为存储字符串（空集合存为清空标记）。</summary>
    public static string SerializeGameSignSelection(IReadOnlyCollection<long> selected)
    {
        return selected.Count == 0 ? NoneSelectionSentinel : string.Join(',', selected);
    }

    /// <summary>面板中单个游戏一行：游戏名 + 各角色名(RoleId) Lv.等级。</summary>
    private static string FormatGameRolesLine(KuroAccount account, long gameId)
    {
        var gameName = KuroGameNames.Format(gameId, account.Roles.FirstOrDefault(role => role.GameId == gameId)?.GameName ?? string.Empty);
        var details = account.Roles
            .Where(role => role.GameId == gameId)
            .Select(role => $"{role.RoleName}({role.RoleId}){(string.IsNullOrWhiteSpace(role.GameLevel) ? string.Empty : " Lv." + role.GameLevel)}")
            .ToArray();
        return details.Length == 0 ? $"- {gameName}" : $"- {gameName}：{string.Join("、", details)}";
    }

    // ---- Telegram MarkdownV2 富文本（原 TelegramGateway KuroTelegramRenderer 逻辑迁入） ----

    private static string RenderAccountListMarkdown(IReadOnlyList<KuroAccount> accounts)
    {
        if (accounts.Count == 0)
        {
            return "尚未绑定库街区账号";
        }

        var lines = new List<string> { MarkdownV2.Escape("[库街区]"), "已绑定账号：" };
        foreach (var account in accounts)
        {
            lines.Add($"\\- `#{account.Id}` `{MarkdownV2.Code(account.DisplayName)}` \\({account.BbsUserId}\\)：自动签到{MarkdownV2.Escape(account.AutoSignEnabled ? "开启" : "关闭")}");
            foreach (var role in account.Roles)
            {
                lines.Add($"  \\- {MarkdownV2.Escape(role.GameName)} / `{MarkdownV2.Code(role.RoleName)}`：{MarkdownV2.Escape(role.AutoSignEnabled ? "自动签到开启" : "自动签到关闭")}");
            }
        }

        return string.Join('\n', lines);
    }

    private static string RenderBindResultMarkdown(KuroBindResult result)
    {
        var account = result.Account;
        var lines = new List<string>
        {
            MarkdownV2.Escape(result.UpdatedExisting ? "库街区账号已更新" : "库街区账号绑定成功"),
            $"账号：`#{account.Id}` `{MarkdownV2.Code(account.DisplayName)}`",
            $"UID：`{account.BbsUserId}`"
        };
        if (account.Roles.Count > 0)
        {
            lines.Add("角色：");
            lines.AddRange(account.Roles.Select(role => $"\\- {MarkdownV2.Escape(role.GameName)} / `{MarkdownV2.Code(role.RoleName)}` \\(Lv\\.{MarkdownV2.Escape(role.GameLevel)}\\)"));
        }

        return string.Join('\n', lines);
    }

    private string RenderSignResultMarkdown(string title, KuroAccount account, IReadOnlyList<string> resultLines)
    {
        var lines = new List<string>
        {
            MarkdownV2.Escape(title),
            $"账号：`#{account.Id}` `{MarkdownV2.Code(account.DisplayName)}`"
        };
        lines.AddRange(resultLines.Select(MarkdownV2.Escape));
        var occurredAt = timeProvider.GetUtcNow().ToLocalTime();
        lines.Add($"时间：{MarkdownV2.Escape(occurredAt.ToString("yyyy-MM-dd HH:mm:ss"))}");
        return string.Join('\n', lines);
    }

    private async Task<ResponseButton> ButtonAsync(
        CommandContext context, string actionType, string text, object data, CancellationToken cancellationToken)
    {
        return new ResponseButton
        {
            Text = text,
            Payload = await callbackStore.PutAsync(
                actionType, context.Identity.CoreUserId, context.Request.ChatId, context.Request.UserId,
                data, cancellationToken: cancellationToken)
        };
    }

    private static ResponseButtonRow SingleButtonRow(ResponseButton button)
    {
        return new ResponseButtonRow { Buttons = { button } };
    }

    private static void ApplyEdit(CommandResponse response, string? editMessageId)
    {
        if (!string.IsNullOrWhiteSpace(editMessageId))
        {
            response.AsTelegramEdit(editMessageId);
        }
    }

    public async Task<CommandResponse> BuildDeletePanelAsync(
        CommandContext context,
        IReadOnlyList<KuroAccount> accounts,
        CancellationToken cancellationToken = default)
    {
        var response = CommandResponses.Text("请选择要删除的库街区账号：", context);
        foreach (var account in accounts)
        {
            response.AddButtonRow(new ResponseButtonRow
            {
                Buttons =
                {
                    new ResponseButton
                    {
                        Text = $"{account.DisplayName} #{account.Id}",
                        Payload = await callbackStore.PutAsync(
                            "kuro-delete-select",
                            context.Identity.CoreUserId,
                            context.Request.ChatId,
                            context.Request.UserId,
                            new KuroAccountCallbackData(account.Id),
                            cancellationToken: cancellationToken)
                    }
                }
            });
        }

        return response;
    }

    public async Task<CommandResponse> BuildAutoSignPanelAsync(
        CommandContext context,
        IReadOnlyList<KuroAccount> accounts,
        string? editMessageId = null,
        CancellationToken cancellationToken = default,
        int page = 0)
    {
        page = NormalizePage(page, accounts.Count, AccountsPerPage);
        var response = CommandResponses.Text(BuildAutoSignText(accounts, page), context);
        ApplyEdit(response, editMessageId);

        await AddPagedAccountButtonsAsync(
            response,
            context,
            accounts,
            "kuro-autosign-account-menu",
            account => new KuroAutoSignMenuCallbackData(account.Id, "account"),
            page,
            AccountsPerPage,
            cancellationToken);
        await AddPageNavigationButtonsAsync(
            response,
            context,
            "kuro-autosign-root-menu",
            accountId: 0,
            level: "root",
            page,
            totalCount: accounts.Count,
            pageSize: AccountsPerPage,
            cancellationToken);
        return await Task.FromResult(response);
    }

    public async Task<CommandResponse> BuildAutoSignAccountPanelAsync(
        CommandContext context,
        IReadOnlyList<KuroAccount> accounts,
        long accountId,
        string? editMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var account = accounts.FirstOrDefault(item => item.Id == accountId);
        if (account is null)
        {
            return CommandResponses.Error("KuroAccountMissing", "未找到指定库街区账号", context);
        }

        var response = CommandResponses.Text(BuildAutoSignAccountDetailText(account), context);
        ApplyEdit(response, editMessageId);

        response.AddButtonRow(new ResponseButtonRow
        {
            Buttons =
            {
                new ResponseButton
                {
                    Text = account.AutoSignEnabled ? "[开] 总开关" : "[关] 总开关",
                    Payload = await callbackStore.PutAsync(
                        "kuro-auto-sign-toggle",
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        new KuroAutoSignCallbackData(account.Id),
                        cancellationToken: cancellationToken)
                }
            }
        });
        response.AddButtonRow(new ResponseButtonRow
        {
            Buttons =
            {
                new ResponseButton
                {
                    Text = "库街区",
                    Payload = await callbackStore.PutAsync(
                        "kuro-autosign-bbs-menu",
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        new KuroAutoSignMenuCallbackData(account.Id, "bbs"),
                        cancellationToken: cancellationToken)
                },
                new ResponseButton
                {
                    Text = "游戏角色",
                    Payload = await callbackStore.PutAsync(
                        "kuro-autosign-game-menu",
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        new KuroAutoSignMenuCallbackData(account.Id, "game"),
                        cancellationToken: cancellationToken)
                }
            }
        });
        response.AddButtonRow(await BackRowAsync(context, "返回账号列表", "kuro-autosign-root-menu", new KuroAutoSignMenuCallbackData(0, "root"), cancellationToken));
        return response;
    }

    public async Task<CommandResponse> BuildAutoSignBbsPanelAsync(
        CommandContext context,
        IReadOnlyList<KuroAccount> accounts,
        long accountId,
        string? editMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var account = accounts.FirstOrDefault(item => item.Id == accountId);
        if (account is null)
        {
            return CommandResponses.Error("KuroAccountMissing", "未找到指定库街区账号", context);
        }

        var response = CommandResponses.Text(BuildAutoSignBbsText(account), context);
        ApplyEdit(response, editMessageId);

        response.AddButtonRow(new ResponseButtonRow
        {
            Buttons =
            {
                await TaskButtonAsync(context, account, KuroBbsTaskFlags.SignIn, "签到", cancellationToken),
                await TaskButtonAsync(context, account, KuroBbsTaskFlags.ViewPosts, "浏览", cancellationToken)
            }
        });
        response.AddButtonRow(new ResponseButtonRow
        {
            Buttons =
            {
                await TaskButtonAsync(context, account, KuroBbsTaskFlags.LikePosts, "点赞", cancellationToken),
                await TaskButtonAsync(context, account, KuroBbsTaskFlags.SharePosts, "分享", cancellationToken)
            }
        });
        response.AddButtonRow(new ResponseButtonRow
        {
            Buttons =
            {
                new ResponseButton
                {
                    Text = "开启/关闭全部",
                    Payload = await callbackStore.PutAsync(
                        "kuro-bbs-task-toggle-all",
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        new KuroBbsTaskToggleAllCallbackData(account.Id),
                        cancellationToken: cancellationToken)
                }
            }
        });
        response.AddButtonRow(await BackRowAsync(context, "返回", "kuro-autosign-account-menu", new KuroAutoSignMenuCallbackData(account.Id, "account"), cancellationToken));
        return response;
    }

    public async Task<CommandResponse> BuildAutoSignGamePanelAsync(
        CommandContext context,
        IReadOnlyList<KuroAccount> accounts,
        long accountId,
        string? editMessageId = null,
        CancellationToken cancellationToken = default,
        int page = 0)
    {
        var account = accounts.FirstOrDefault(item => item.Id == accountId);
        if (account is null)
        {
            return CommandResponses.Error("KuroAccountMissing", "未找到指定库街区账号", context);
        }

        var orderedRoles = account.Roles.OrderBy(role => role.GameId).ThenBy(role => role.RoleId).ToArray();
        page = NormalizePage(page, orderedRoles.Length, RolesPerPage);
        var response = CommandResponses.Text(BuildAutoSignGameText(account, page), context);
        ApplyEdit(response, editMessageId);

        await AddPagedRoleButtonsAsync(response, context, account, orderedRoles, page, RolesPerPage, cancellationToken);
        await AddPageNavigationButtonsAsync(
            response,
            context,
            "kuro-autosign-game-menu",
            account.Id,
            level: "game",
            page,
            totalCount: orderedRoles.Length,
            pageSize: RolesPerPage,
            cancellationToken);
        response.AddButtonRow(new ResponseButtonRow
        {
            Buttons =
            {
                new ResponseButton
                {
                    Text = "开启/关闭全部",
                    Payload = await callbackStore.PutAsync(
                        "kuro-game-auto-sign-toggle-all",
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        new KuroGameAutoSignToggleAllCallbackData(account.Id, page),
                        cancellationToken: cancellationToken)
                }
            }
        });
        response.AddButtonRow(await BackRowAsync(context, "返回", "kuro-autosign-account-menu", new KuroAutoSignMenuCallbackData(account.Id, "account"), cancellationToken));
        return response;
    }

    private async Task AddPagedAccountButtonsAsync(
        CommandResponse response,
        CommandContext context,
        IReadOnlyList<KuroAccount> accounts,
        string actionType,
        Func<KuroAccount, object> dataFactory,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var row = new ResponseButtonRow();
        foreach (var account in accounts.Skip(page * pageSize).Take(pageSize))
        {
            row.Buttons.Add(new ResponseButton
            {
                Text = $"{(account.AutoSignEnabled ? "[开]" : "[关]")} {account.DisplayName}",
                Payload = await callbackStore.PutAsync(
                    actionType,
                    context.Identity.CoreUserId,
                    context.Request.ChatId,
                    context.Request.UserId,
                    dataFactory(account),
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
    }

    private async Task AddPagedRoleButtonsAsync(
        CommandResponse response,
        CommandContext context,
        KuroAccount account,
        IReadOnlyList<KuroGameRole> roles,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var row = new ResponseButtonRow();
        foreach (var role in roles.Skip(page * pageSize).Take(pageSize))
        {
            row.Buttons.Add(new ResponseButton
            {
                Text = $"{(role.AutoSignEnabled ? "[开]" : "[关]")} {role.GameName}/{role.RoleName}",
                Payload = await callbackStore.PutAsync(
                    "kuro-game-auto-sign-toggle",
                    context.Identity.CoreUserId,
                    context.Request.ChatId,
                    context.Request.UserId,
                    new KuroGameAutoSignCallbackData(role.Id, account.Id, page),
                    cancellationToken: cancellationToken)
            });

            if (row.Buttons.Count == 1)
            {
                response.AddButtonRow(row);
                row = new ResponseButtonRow();
            }
        }
    }

    private async Task AddPageNavigationButtonsAsync(
        CommandResponse response,
        CommandContext context,
        string actionType,
        long accountId,
        string level,
        int page,
        int totalCount,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var totalPages = GetTotalPages(totalCount, pageSize);
        if (totalPages <= 1)
        {
            return;
        }

        var row = new ResponseButtonRow();
        if (page > 0)
        {
            row.Buttons.Add(new ResponseButton
            {
                Text = "上一页",
                Payload = await callbackStore.PutAsync(
                    actionType,
                    context.Identity.CoreUserId,
                    context.Request.ChatId,
                    context.Request.UserId,
                    new KuroAutoSignMenuCallbackData(accountId, level, page - 1),
                    cancellationToken: cancellationToken)
            });
        }

        if (page + 1 < totalPages)
        {
            row.Buttons.Add(new ResponseButton
            {
                Text = "下一页",
                Payload = await callbackStore.PutAsync(
                    actionType,
                    context.Identity.CoreUserId,
                    context.Request.ChatId,
                    context.Request.UserId,
                    new KuroAutoSignMenuCallbackData(accountId, level, page + 1),
                    cancellationToken: cancellationToken)
            });
        }

        if (row.Buttons.Count > 0)
        {
            response.AddButtonRow(row);
        }
    }

    private async Task<ResponseButtonRow> BackRowAsync(
        CommandContext context,
        string text,
        string actionType,
        object data,
        CancellationToken cancellationToken)
    {
        return new ResponseButtonRow
        {
            Buttons =
            {
                new ResponseButton
                {
                    Text = text,
                    Payload = await callbackStore.PutAsync(
                        actionType,
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        data,
                        cancellationToken: cancellationToken)
                }
            }
        };
    }

    public async Task<CommandResponse> BuildNotifyTypePanelAsync(
        CommandContext context,
        IReadOnlyList<KuroAccount> accounts,
        string? editMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var enabled = await subscriptionService.GetEnabledTargetIdsAsync(
            context.Identity.CoreUserId,
            context.Request.Platform,
            NotificationTypes.KuroAutoSign,
            accounts.Select(account => account.Id).ToArray(),
            cancellationToken);
        var enabledMarks = enabled.Count > 0
            ? new[] { $"`{MarkdownV2.Code(NotificationTypes.KuroAutoSignDisplayName)}`" }
            : [];
        var markdown = string.Join('\n',
            MarkdownV2.Escape("[消息订阅管理]"),
            MarkdownV2.Escape("当前已启用：") + (enabledMarks.Length == 0
                ? MarkdownV2.Escape("无")
                : string.Join(MarkdownV2.Escape("、"), enabledMarks)));
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
                    Text = NotificationTypes.KuroAutoSignDisplayName,
                    Payload = await callbackStore.PutAsync(
                        "notify-type-select",
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        new NotifyTypeCallbackData(NotificationTypes.KuroAutoSign),
                        cancellationToken: cancellationToken)
                }
            }
        });
        return response;
    }

    public async Task<CommandResponse> BuildNotifyAccountPanelAsync(
        CommandContext context,
        IReadOnlyList<KuroAccount> accounts,
        string? editMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var enabled = await subscriptionService.GetEnabledTargetIdsAsync(
            context.Identity.CoreUserId,
            context.Request.Platform,
            NotificationTypes.KuroAutoSign,
            accounts.Select(account => account.Id).ToArray(),
            cancellationToken);
        var enabledMarks = accounts
            .Where(account => enabled.Contains(account.Id))
            .Select(account => $"`{MarkdownV2.Code(account.DisplayName)}`")
            .ToArray();
        var markdown = string.Join('\n',
            MarkdownV2.Escape($"[消息订阅 · {NotificationTypes.KuroAutoSignDisplayName}]"),
            MarkdownV2.Escape("当前已启用：") + (enabledMarks.Length == 0
                ? MarkdownV2.Escape("无")
                : string.Join(MarkdownV2.Escape("、"), enabledMarks)),
            MarkdownV2.Escape("此处为消息订阅管理，仅控制签到结果是否推送；开关自动签到请使用 ") + MarkdownV2.CodeSpan("/kuro autosign"));
        var response = CommandResponses.TelegramMarkdown(
            context.Identity,
            markdown,
            replyToMessageId: editMessageId is null ? context.Request.MessageId : null,
            editMessageId: editMessageId);

        var row = new ResponseButtonRow();
        foreach (var account in accounts)
        {
            row.Buttons.Add(new ResponseButton
            {
                Text = $"{(enabled.Contains(account.Id) ? "[开]" : "[关]")} {account.DisplayName}",
                Payload = await callbackStore.PutAsync(
                    "notify-account-toggle",
                    context.Identity.CoreUserId,
                    context.Request.ChatId,
                    context.Request.UserId,
                    new NotifyAccountCallbackData(NotificationTypes.KuroAutoSign, account.Id, ToggleAll: false),
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

        response.AddButtonRow(new ResponseButtonRow
        {
            Buttons =
            {
                new ResponseButton
                {
                    Text = "开启/关闭全部",
                    Payload = await callbackStore.PutAsync(
                        "notify-account-toggle",
                        context.Identity.CoreUserId,
                        context.Request.ChatId,
                        context.Request.UserId,
                        new NotifyAccountCallbackData(NotificationTypes.KuroAutoSign, 0, ToggleAll: true),
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

    private async Task<ResponseButton> TaskButtonAsync(
        CommandContext context,
        KuroAccount account,
        long taskFlag,
        string text,
        CancellationToken cancellationToken)
    {
        return new ResponseButton
        {
            Text = $"{(((account.BbsTaskFlags & taskFlag) != 0) ? "[开]" : "[关]")} {text}",
            Payload = await callbackStore.PutAsync(
                "kuro-bbs-task-toggle",
                context.Identity.CoreUserId,
                context.Request.ChatId,
                context.Request.UserId,
                new KuroBbsTaskCallbackData(account.Id, taskFlag),
                cancellationToken: cancellationToken)
        };
    }

    private static string BuildAutoSignText(IReadOnlyList<KuroAccount> accounts, int page)
    {
        if (accounts.Count == 0)
        {
            return "尚未绑定库街区账号";
        }

        var totalPages = GetTotalPages(accounts.Count, AccountsPerPage);
        var lines = new List<string> { "[库街区自动签到管理]", $"请选择账号（第 {page + 1}/{totalPages} 页）：" };
        foreach (var account in accounts.Skip(page * AccountsPerPage).Take(AccountsPerPage))
        {
            lines.Add($"#{account.Id} {account.DisplayName}：{(account.AutoSignEnabled ? "开启" : "关闭")}");
        }

        lines.Add("如需管理签到结果的消息推送，请使用 `/notify`");
        return string.Join('\n', lines);
    }

    private static string BuildAutoSignAccountDetailText(KuroAccount account)
    {
        return string.Join('\n',
            "[库街区自动签到管理]",
            $"账号：#{account.Id} {account.DisplayName}",
            $"总开关：{(account.AutoSignEnabled ? "开启" : "关闭")}",
            $"库街区：{FormatBbsTasks(account.BbsTaskFlags)}",
            "游戏角色：" + FormatGameRoles(account));
    }

    private static string BuildAutoSignBbsText(KuroAccount account)
    {
        return string.Join('\n',
            "[库街区自动签到 - 库街区]",
            $"账号：#{account.Id} {account.DisplayName}",
            $"当前已启用：{FormatBbsTasks(account.BbsTaskFlags)}");
    }

    private static string BuildAutoSignGameText(KuroAccount account, int page)
    {
        var totalPages = GetTotalPages(account.Roles.Count, RolesPerPage);
        return string.Join('\n',
            "[库街区自动签到 - 游戏角色]",
            $"账号：#{account.Id} {account.DisplayName}",
            $"第 {page + 1}/{totalPages} 页",
            "当前已启用：" + FormatGameRoles(account, onlyEnabled: true));
    }

    private static string FormatGameRoles(KuroAccount account, bool onlyEnabled = false)
    {
        var roles = account.Roles
            .Where(role => !onlyEnabled || role.AutoSignEnabled)
            .Select(role => $"{role.GameName}/{role.RoleName}")
            .ToArray();
        return roles.Length == 0 ? "无" : string.Join("、", roles);
    }

    private static string FormatBbsTasks(long flags)
    {
        var enabled = new List<string>();
        if ((flags & KuroBbsTaskFlags.SignIn) != 0) enabled.Add("签到");
        if ((flags & KuroBbsTaskFlags.ViewPosts) != 0) enabled.Add("浏览");
        if ((flags & KuroBbsTaskFlags.LikePosts) != 0) enabled.Add("点赞");
        if ((flags & KuroBbsTaskFlags.SharePosts) != 0) enabled.Add("分享");
        return enabled.Count == 0 ? "无" : string.Join("、", enabled);
    }

    private static int NormalizePage(int page, int totalCount, int pageSize)
    {
        var totalPages = GetTotalPages(totalCount, pageSize);
        return Math.Clamp(page, 0, totalPages - 1);
    }

    private static int GetTotalPages(int totalCount, int pageSize)
    {
        return Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
    }
}
