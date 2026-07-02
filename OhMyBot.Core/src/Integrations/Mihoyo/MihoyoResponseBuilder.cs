using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Integrations.AiRouter;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Infrastructure.Data.Entities;
using OhMyBot.Core.Commanding.Notifications;

namespace OhMyBot.Core.Integrations.Mihoyo;

public sealed class MihoyoResponseBuilder(
    CallbackActionStore callbackStore,
    NotificationSubscriptionService subscriptionService,
    TimeProvider timeProvider)
{
    private const int AccountsPerPage = 8;
    private const int RolesPerPage = 6;

    public Task<CommandResponse> BuildAccountListAsync(
        CommandContext context,
        IReadOnlyList<MihoyoAccount> accounts,
        CancellationToken cancellationToken = default)
    {
        var response = CommandResponses.Ok(CommandResponseDataKind.MihoyoAccountList, context);
        response.MihoyoAccountList = new MihoyoAccountListData();
        response.MihoyoAccountList.Accounts.AddRange(accounts.Select(account => ToItem(account, notificationEnabled: false)));
        response.Message = accounts.Count == 0
            ? "尚未绑定米游社账号"
            : "[米游社]\n已绑定账号：\n" + string.Join('\n', accounts.Select(account =>
                $"- #{account.Id} {account.DisplayName} [{RegionLabel(account.Region)}]：自动签到{(account.AutoSignEnabled ? "开启" : "关闭")}"));
        return Task.FromResult(response);
    }

    public CommandResponse BuildBindResult(CommandContext context, MihoyoBindResult result)
    {
        var response = CommandResponses.Ok(CommandResponseDataKind.MihoyoBindResult, context);
        response.MihoyoBindResult = new MihoyoBindResultData
        {
            Account = ToItem(result.Account, notificationEnabled: false),
            UpdatedExisting = result.UpdatedExisting
        };
        response.Message = result.UpdatedExisting ? "米游社账号已更新" : "米游社账号绑定成功";
        return response;
    }

    public CommandResponse BuildBbsSignResult(CommandContext context, MihoyoBbsSignResult result, bool autoSign = false)
    {
        var response = CommandResponses.Ok(CommandResponseDataKind.MihoyoBbsSignResult, context);
        response.MihoyoBbsSignResult = new MihoyoBbsSignResultData
        {
            Account = ToItem(result.Account, notificationEnabled: false),
            AutoSign = autoSign,
            OccurredAtUnixSeconds = timeProvider.GetUtcNow().ToUnixTimeSeconds()
        };
        response.MihoyoBbsSignResult.Lines.AddRange(result.Lines);
        response.Message = string.Join('\n', result.Lines);
        return response;
    }

    public CommandResponse BuildGameSignResult(CommandContext context, MihoyoGameSignResult result, bool autoSign = false)
    {
        var response = CommandResponses.Ok(CommandResponseDataKind.MihoyoGameSignResult, context);
        response.MihoyoGameSignResult = new MihoyoGameSignResultData
        {
            Account = ToItem(result.Account, notificationEnabled: false),
            AutoSign = autoSign,
            OccurredAtUnixSeconds = timeProvider.GetUtcNow().ToUnixTimeSeconds()
        };
        response.MihoyoGameSignResult.Lines.AddRange(result.Lines);
        response.Message = string.Join('\n', result.Lines);
        return response;
    }

    public async Task<CommandResponse> BuildBbsSignSelectionAsync(
        CommandContext context,
        IReadOnlyList<MihoyoAccount> accounts,
        IReadOnlyList<string> actions,
        string? editMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var response = CommandResponses.Text("请选择要执行社区任务的米游社账号（仅国服）：", context);
        ApplyEdit(response, editMessageId);
        var cnAccounts = accounts.Where(account => account.Region == MihoyoRegion.Cn).ToArray();
        foreach (var account in cnAccounts)
        {
            response.ButtonRows.Add(SingleButtonRow(await ButtonAsync(
                context, "mihoyo-bbs-sign-select", $"{account.DisplayName} #{account.Id}",
                new MihoyoBbsSignCallbackData(account.Id, actions.ToArray()), cancellationToken)));
        }

        if (cnAccounts.Length > 1)
        {
            response.ButtonRows.Add(SingleButtonRow(await ButtonAsync(
                context, "mihoyo-bbs-sign-all", "全部签到",
                new MihoyoBbsSignAllCallbackData(), cancellationToken)));
        }

        return response;
    }

    public async Task<CommandResponse> BuildGameSignSelectionAsync(
        CommandContext context,
        IReadOnlyList<MihoyoAccount> accounts,
        string? editMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var response = CommandResponses.Text("请选择要执行游戏签到的米游社账号：", context);
        ApplyEdit(response, editMessageId);
        foreach (var account in accounts)
        {
            response.ButtonRows.Add(SingleButtonRow(await ButtonAsync(
                context, "mihoyo-game-sign-panel", $"{account.DisplayName} #{account.Id} [{RegionLabel(account.Region)}]",
                new MihoyoGameSignPanelCallbackData(account.Id), cancellationToken)));
        }

        if (accounts.Count > 1)
        {
            response.ButtonRows.Add(SingleButtonRow(await ButtonAsync(
                context, "mihoyo-game-sign-all", "全部签到",
                new MihoyoGameSignAllCallbackData(), cancellationToken)));
        }

        return response;
    }

    /// <summary>
    /// 游戏签到勾选面板：每个游戏一个开关按钮（√ 签到 / × 跳过），底部为「签到」「返回」。
    /// </summary>
    public async Task<CommandResponse> BuildGameSignPanelAsync(
        CommandContext context,
        MihoyoAccount account,
        IReadOnlyCollection<string> selected,
        string? editMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var available = AvailableGameKeys(account);
        if (available.Count == 0)
        {
            var empty = CommandResponses.Text(
                $"账号 #{account.Id} {account.DisplayName} 暂无游戏角色，请先使用 /mihoyo game init {account.Id} 同步", context);
            ApplyEdit(empty, editMessageId);
            return empty;
        }

        var selectedSet = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>
        {
            "[米游社游戏签到]",
            $"账号：#{account.Id} {account.DisplayName} [{RegionLabel(account.Region)}]",
            "勾选要签到的游戏（√=签到 ×=跳过），然后点击「签到」："
        };
        lines.AddRange(available.Select(key => FormatGameRolesLine(account, MihoyoGameCatalog.FindByKey(key)!)));
        var response = CommandResponses.Text(string.Join('\n', lines), context);
        ApplyEdit(response, editMessageId);

        foreach (var key in available)
        {
            var game = MihoyoGameCatalog.FindByKey(key)!;
            response.ButtonRows.Add(SingleButtonRow(await ButtonAsync(
                context, "mihoyo-game-sign-panel", $"{(selectedSet.Contains(key) ? "[√]" : "[×]")} {game.Name}",
                new MihoyoGameSignPanelCallbackData(account.Id, Toggle: key), cancellationToken)));
        }

        response.ButtonRows.Add(new ResponseButtonRow
        {
            Buttons =
            {
                await ButtonAsync(context, "mihoyo-game-sign-run", "签到",
                    new MihoyoGameSignPanelCallbackData(account.Id), cancellationToken),
                await ButtonAsync(context, "mihoyo-game-sign-back", "返回",
                    new MihoyoGameSignBackCallbackData(), cancellationToken)
            }
        });
        return response;
    }

    /// <summary>
    /// 全部账号游戏签到的合并结果（纯文本）。
    /// </summary>
    public CommandResponse BuildCombinedResult(
        CommandContext context,
        string title,
        IReadOnlyList<(MihoyoAccount Account, IReadOnlyList<string> Lines)> results,
        string? editMessageId = null)
    {
        var blocks = new List<string> { title };
        foreach (var (account, lines) in results)
        {
            blocks.Add(string.Empty);
            blocks.Add($"#{account.Id} {account.DisplayName} [{RegionLabel(account.Region)}]");
            blocks.AddRange(lines.Count == 0 ? ["无结果"] : lines);
        }

        blocks.Add(string.Empty);
        blocks.Add("时间：" + timeProvider.GetUtcNow().ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        var response = CommandResponses.Text(string.Join('\n', blocks), context);
        ApplyEdit(response, editMessageId);
        return response;
    }

    /// <summary>账号已同步角色去重后的游戏 Key，按目录顺序排列。</summary>
    public static IReadOnlyList<string> AvailableGameKeys(MihoyoAccount account)
    {
        var owned = account.Roles
            .Select(role => MihoyoGameCatalog.FindByGameBiz(role.GameBiz)?.Key)
            .Where(key => key is not null)
            .Select(key => key!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return MihoyoGameCatalog.Games
            .Where(game => owned.Contains(game.Key))
            .Select(game => game.Key)
            .ToArray();
    }

    /// <summary>“显式清空（无勾选）”的存储标记，用于与“未设置（默认全选）”区分。</summary>
    public const string NoneSelectionSentinel = "-";

    /// <summary>面板初始勾选：账号上次持久化的选择（与当前可用游戏取交集）；未设置时默认全选，显式清空则为空。</summary>
    public static IReadOnlyList<string> ResolveGameSignSelection(MihoyoAccount account)
    {
        var available = AvailableGameKeys(account);
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
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return available.Where(key => stored.Contains(key)).ToArray();
    }

    /// <summary>把勾选集合序列化为存储字符串（空集合存为清空标记）。</summary>
    public static string SerializeGameSignSelection(IReadOnlyCollection<string> selected)
    {
        return selected.Count == 0 ? NoneSelectionSentinel : string.Join(',', selected);
    }

    public async Task<CommandResponse> BuildDeletePanelAsync(
        CommandContext context,
        IReadOnlyList<MihoyoAccount> accounts,
        CancellationToken cancellationToken = default)
    {
        var response = CommandResponses.Text("请选择要删除的米游社账号：", context);
        foreach (var account in accounts)
        {
            response.ButtonRows.Add(SingleButtonRow(await ButtonAsync(
                context, "mihoyo-delete-select", $"{account.DisplayName} #{account.Id} [{RegionLabel(account.Region)}]",
                new MihoyoAccountCallbackData(account.Id), cancellationToken)));
        }

        return response;
    }

    public async Task<CommandResponse> BuildAutoSignPanelAsync(
        CommandContext context,
        IReadOnlyList<MihoyoAccount> accounts,
        string? editMessageId = null,
        CancellationToken cancellationToken = default,
        int page = 0)
    {
        page = NormalizePage(page, accounts.Count, AccountsPerPage);
        var response = CommandResponses.Text(BuildAutoSignText(accounts, page), context);
        ApplyEdit(response, editMessageId);

        var row = new ResponseButtonRow();
        foreach (var account in accounts.Skip(page * AccountsPerPage).Take(AccountsPerPage))
        {
            row.Buttons.Add(await ButtonAsync(
                context, "mihoyo-autosign-account-menu", $"{(account.AutoSignEnabled ? "[开]" : "[关]")} {account.DisplayName}",
                new MihoyoAutoSignMenuCallbackData(account.Id, "account"), cancellationToken));
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

        await AddPageNavigationAsync(response, context, "mihoyo-autosign-root-menu", 0, "root", page, accounts.Count, AccountsPerPage, cancellationToken);
        return response;
    }

    public async Task<CommandResponse> BuildAutoSignAccountPanelAsync(
        CommandContext context,
        IReadOnlyList<MihoyoAccount> accounts,
        long accountId,
        string? editMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var account = accounts.FirstOrDefault(item => item.Id == accountId);
        if (account is null)
        {
            return CommandResponses.Error("MihoyoAccountMissing", "未找到指定米游社账号", context);
        }

        var response = CommandResponses.Text(BuildAutoSignAccountDetailText(account), context);
        ApplyEdit(response, editMessageId);

        response.ButtonRows.Add(SingleButtonRow(await ButtonAsync(
            context, "mihoyo-auto-sign-toggle", account.AutoSignEnabled ? "[开] 总开关" : "[关] 总开关",
            new MihoyoAutoSignCallbackData(account.Id), cancellationToken)));

        var menuRow = new ResponseButtonRow();
        if (account.Region == MihoyoRegion.Cn)
        {
            menuRow.Buttons.Add(await ButtonAsync(
                context, "mihoyo-autosign-bbs-menu", "社区任务",
                new MihoyoAutoSignMenuCallbackData(account.Id, "bbs"), cancellationToken));
        }

        menuRow.Buttons.Add(await ButtonAsync(
            context, "mihoyo-autosign-game-menu", "游戏角色",
            new MihoyoAutoSignMenuCallbackData(account.Id, "game"), cancellationToken));
        response.ButtonRows.Add(menuRow);

        response.ButtonRows.Add(SingleButtonRow(await ButtonAsync(
            context, "mihoyo-autosign-root-menu", "返回账号列表",
            new MihoyoAutoSignMenuCallbackData(0, "root"), cancellationToken)));
        return response;
    }

    public async Task<CommandResponse> BuildAutoSignBbsPanelAsync(
        CommandContext context,
        IReadOnlyList<MihoyoAccount> accounts,
        long accountId,
        string? editMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var account = accounts.FirstOrDefault(item => item.Id == accountId);
        if (account is null)
        {
            return CommandResponses.Error("MihoyoAccountMissing", "未找到指定米游社账号", context);
        }

        var response = CommandResponses.Text(BuildAutoSignBbsText(account), context);
        ApplyEdit(response, editMessageId);

        response.ButtonRows.Add(new ResponseButtonRow
        {
            Buttons =
            {
                await TaskButtonAsync(context, account, MihoyoBbsTaskFlags.SignIn, "签到", cancellationToken),
                await TaskButtonAsync(context, account, MihoyoBbsTaskFlags.ViewPosts, "浏览", cancellationToken)
            }
        });
        response.ButtonRows.Add(new ResponseButtonRow
        {
            Buttons =
            {
                await TaskButtonAsync(context, account, MihoyoBbsTaskFlags.LikePosts, "点赞", cancellationToken),
                await TaskButtonAsync(context, account, MihoyoBbsTaskFlags.SharePosts, "分享", cancellationToken)
            }
        });
        response.ButtonRows.Add(SingleButtonRow(await ButtonAsync(
            context, "mihoyo-bbs-task-toggle-all", "开启/关闭全部",
            new MihoyoBbsTaskToggleAllCallbackData(account.Id), cancellationToken)));
        response.ButtonRows.Add(SingleButtonRow(await ButtonAsync(
            context, "mihoyo-autosign-account-menu", "返回",
            new MihoyoAutoSignMenuCallbackData(account.Id, "account"), cancellationToken)));
        return response;
    }

    public async Task<CommandResponse> BuildAutoSignGamePanelAsync(
        CommandContext context,
        IReadOnlyList<MihoyoAccount> accounts,
        long accountId,
        string? editMessageId = null,
        CancellationToken cancellationToken = default,
        int page = 0)
    {
        var account = accounts.FirstOrDefault(item => item.Id == accountId);
        if (account is null)
        {
            return CommandResponses.Error("MihoyoAccountMissing", "未找到指定米游社账号", context);
        }

        var orderedRoles = account.Roles.OrderBy(role => role.GameBiz).ThenBy(role => role.GameUid).ToArray();
        page = NormalizePage(page, orderedRoles.Length, RolesPerPage);
        var response = CommandResponses.Text(BuildAutoSignGameText(account, page), context);
        ApplyEdit(response, editMessageId);

        foreach (var role in orderedRoles.Skip(page * RolesPerPage).Take(RolesPerPage))
        {
            response.ButtonRows.Add(SingleButtonRow(await ButtonAsync(
                context, "mihoyo-game-auto-sign-toggle", $"{(role.AutoSignEnabled ? "[开]" : "[关]")} {FormatRole(role)}",
                new MihoyoGameAutoSignCallbackData(role.Id, account.Id, page), cancellationToken)));
        }

        await AddPageNavigationAsync(response, context, "mihoyo-autosign-game-menu", account.Id, "game", page, orderedRoles.Length, RolesPerPage, cancellationToken);
        response.ButtonRows.Add(SingleButtonRow(await ButtonAsync(
            context, "mihoyo-game-auto-sign-toggle-all", "开启/关闭全部",
            new MihoyoGameAutoSignToggleAllCallbackData(account.Id, page), cancellationToken)));
        response.ButtonRows.Add(SingleButtonRow(await ButtonAsync(
            context, "mihoyo-autosign-account-menu", "返回",
            new MihoyoAutoSignMenuCallbackData(account.Id, "account"), cancellationToken)));
        return response;
    }

    public async Task<CommandResponse> BuildNotifyAccountPanelAsync(
        CommandContext context,
        IReadOnlyList<MihoyoAccount> accounts,
        string? editMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var enabled = await subscriptionService.GetEnabledTargetIdsAsync(
            context.Identity.CoreUserId,
            context.Request.Platform,
            NotificationTypes.MihoyoAutoSign,
            accounts.Select(account => account.Id).ToArray(),
            cancellationToken);
        var response = CommandResponses.Ok(CommandResponseDataKind.NotifyAccountPanel, context);
        response.NotifyAccountPanel = new NotifyAccountPanelData
        {
            Type = NotificationTypes.MihoyoAutoSign,
            DisplayName = NotificationTypes.MihoyoAutoSignDisplayName
        };
        response.NotifyAccountPanel.MihoyoAccounts.AddRange(accounts.Select(account => ToItem(account, enabled.Contains(account.Id))));
        response.Message = $"[{NotificationTypes.MihoyoAutoSignDisplayName}]\n当前已启用: " + FormatEnabledAccounts(accounts, enabled);
        ApplyEdit(response, editMessageId);

        var row = new ResponseButtonRow();
        foreach (var account in accounts)
        {
            row.Buttons.Add(await ButtonAsync(
                context, "notify-account-toggle", $"{(enabled.Contains(account.Id) ? "[开]" : "[关]")} {account.DisplayName}",
                new NotifyAccountCallbackData(NotificationTypes.MihoyoAutoSign, account.Id, ToggleAll: false), cancellationToken));
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

        response.ButtonRows.Add(new ResponseButtonRow
        {
            Buttons =
            {
                await ButtonAsync(context, "notify-account-toggle", "开启/关闭全部",
                    new NotifyAccountCallbackData(NotificationTypes.MihoyoAutoSign, 0, ToggleAll: true), cancellationToken),
                await ButtonAsync(context, "notify-back", "返回", new NotifyBackCallbackData(), cancellationToken)
            }
        });
        return response;
    }

    public static MihoyoAccountItem ToItem(MihoyoAccount account, bool notificationEnabled)
    {
        var item = new MihoyoAccountItem
        {
            Id = account.Id,
            Stuid = account.Stuid,
            DisplayName = account.DisplayName,
            Region = (int)account.Region,
            AutoSignEnabled = account.AutoSignEnabled,
            BbsTaskFlags = account.BbsTaskFlags,
            NotificationEnabled = notificationEnabled
        };
        item.Roles.AddRange(account.Roles
            .OrderBy(role => role.GameBiz)
            .ThenBy(role => role.GameUid)
            .Select(role => new MihoyoGameRoleItem
            {
                Id = role.Id,
                GameBiz = role.GameBiz,
                GameName = role.GameName,
                Region = role.Region,
                GameUid = role.GameUid,
                Nickname = role.Nickname,
                Level = role.Level,
                AutoSignEnabled = role.AutoSignEnabled
            }));
        return item;
    }

    private async Task<ResponseButton> TaskButtonAsync(
        CommandContext context, MihoyoAccount account, long taskFlag, string text, CancellationToken cancellationToken)
    {
        return await ButtonAsync(
            context, "mihoyo-bbs-task-toggle", $"{(((account.BbsTaskFlags & taskFlag) != 0) ? "[开]" : "[关]")} {text}",
            new MihoyoBbsTaskCallbackData(account.Id, taskFlag), cancellationToken);
    }

    private async Task<ResponseButton> ButtonAsync(
        CommandContext context, string actionType, string text, object data, CancellationToken cancellationToken)
    {
        return new ResponseButton
        {
            Text = text,
            Payload = await callbackStore.PutAsync(
                actionType,
                context.Identity.CoreUserId,
                context.Request.ChatId,
                context.Request.UserId,
                data,
                cancellationToken: cancellationToken)
        };
    }

    private async Task AddPageNavigationAsync(
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
            row.Buttons.Add(await ButtonAsync(context, actionType, "上一页",
                new MihoyoAutoSignMenuCallbackData(accountId, level, page - 1), cancellationToken));
        }

        if (page + 1 < totalPages)
        {
            row.Buttons.Add(await ButtonAsync(context, actionType, "下一页",
                new MihoyoAutoSignMenuCallbackData(accountId, level, page + 1), cancellationToken));
        }

        if (row.Buttons.Count > 0)
        {
            response.ButtonRows.Add(row);
        }
    }

    private static ResponseButtonRow SingleButtonRow(ResponseButton button)
    {
        return new ResponseButtonRow { Buttons = { button } };
    }

    private static void ApplyEdit(CommandResponse response, string? editMessageId)
    {
        if (!string.IsNullOrWhiteSpace(editMessageId))
        {
            response.EditMessageId = editMessageId;
            response.ReplyToMessageId = string.Empty;
        }
    }

    private static string BuildAutoSignText(IReadOnlyList<MihoyoAccount> accounts, int page)
    {
        if (accounts.Count == 0)
        {
            return "尚未绑定米游社账号";
        }

        var totalPages = GetTotalPages(accounts.Count, AccountsPerPage);
        var lines = new List<string> { "[米游社自动签到管理]", $"请选择账号（第 {page + 1}/{totalPages} 页）：" };
        foreach (var account in accounts.Skip(page * AccountsPerPage).Take(AccountsPerPage))
        {
            lines.Add($"#{account.Id} {account.DisplayName} [{RegionLabel(account.Region)}]：{(account.AutoSignEnabled ? "开启" : "关闭")}");
        }

        return string.Join('\n', lines);
    }

    private static string BuildAutoSignAccountDetailText(MihoyoAccount account)
    {
        var lines = new List<string>
        {
            "[米游社自动签到管理]",
            $"账号：#{account.Id} {account.DisplayName} [{RegionLabel(account.Region)}]",
            $"总开关：{(account.AutoSignEnabled ? "开启" : "关闭")}"
        };
        if (account.Region == MihoyoRegion.Cn)
        {
            lines.Add($"社区任务：{FormatBbsTasks(account.BbsTaskFlags)}");
        }

        lines.Add("游戏角色：" + FormatGameRoles(account));
        return string.Join('\n', lines);
    }

    private static string BuildAutoSignBbsText(MihoyoAccount account)
    {
        return string.Join('\n',
            "[米游社自动签到 - 社区任务]",
            $"账号：#{account.Id} {account.DisplayName}",
            $"当前已启用：{FormatBbsTasks(account.BbsTaskFlags)}");
    }

    private static string BuildAutoSignGameText(MihoyoAccount account, int page)
    {
        var totalPages = GetTotalPages(account.Roles.Count, RolesPerPage);
        return string.Join('\n',
            "[米游社自动签到 - 游戏角色]",
            $"账号：#{account.Id} {account.DisplayName} [{RegionLabel(account.Region)}]",
            $"第 {page + 1}/{totalPages} 页",
            "当前已启用：" + FormatGameRoles(account, onlyEnabled: true));
    }

    private static string FormatRole(MihoyoGameRole role)
    {
        return role.GameUid > 0 ? $"{role.GameName}/{role.Nickname}" : role.GameName;
    }

    /// <summary>面板中单个游戏一行：游戏名 + 各角色昵称(UID) Lv.等级。</summary>
    private static string FormatGameRolesLine(MihoyoAccount account, MihoyoGameDef game)
    {
        var details = account.Roles
            .Where(role => string.Equals(role.GameBiz, game.CnGameBiz, StringComparison.OrdinalIgnoreCase) && role.GameUid > 0)
            .Select(role => $"{role.Nickname}({role.GameUid}){(string.IsNullOrWhiteSpace(role.Level) ? string.Empty : " Lv." + role.Level)}")
            .ToArray();
        return details.Length == 0 ? $"- {game.Name}" : $"- {game.Name}：{string.Join("、", details)}";
    }

    private static string FormatGameRoles(MihoyoAccount account, bool onlyEnabled = false)
    {
        var roles = account.Roles
            .Where(role => !onlyEnabled || role.AutoSignEnabled)
            .Select(FormatRole)
            .ToArray();
        return roles.Length == 0 ? "无" : string.Join("、", roles);
    }

    private static string FormatBbsTasks(long flags)
    {
        var enabled = new List<string>();
        if ((flags & MihoyoBbsTaskFlags.SignIn) != 0) enabled.Add("签到");
        if ((flags & MihoyoBbsTaskFlags.ViewPosts) != 0) enabled.Add("浏览");
        if ((flags & MihoyoBbsTaskFlags.LikePosts) != 0) enabled.Add("点赞");
        if ((flags & MihoyoBbsTaskFlags.SharePosts) != 0) enabled.Add("分享");
        return enabled.Count == 0 ? "无" : string.Join("、", enabled);
    }

    private static string RegionLabel(MihoyoRegion region)
    {
        return region == MihoyoRegion.Cn ? "国服" : "国际服";
    }

    private static string FormatEnabledAccounts(IReadOnlyList<MihoyoAccount> accounts, IReadOnlySet<long> enabled)
    {
        var names = accounts.Where(account => enabled.Contains(account.Id)).Select(account => account.DisplayName).ToArray();
        return names.Length == 0 ? "无" : string.Join("、", names);
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
