using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.AiRouter;
using OhMyBot.Core.Callbacks;
using OhMyBot.Core.Commands;
using OhMyBot.Core.Data.Entities;
using OhMyBot.Core.Notifications;

namespace OhMyBot.Core.Kuro;

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
        var response = CommandResponses.Ok(CommandResponseDataKind.KuroAccountList, context);
        response.KuroAccountList = new KuroAccountListData();
        response.KuroAccountList.Accounts.AddRange(accounts.Select(account => ToItem(account, notificationEnabled: false)));
        response.Message = accounts.Count == 0
            ? "尚未绑定库街区账号"
            : "[库街区]\n已绑定账号：\n" + string.Join('\n', accounts.Select(account => $"- #{account.Id} {account.DisplayName} ({account.BbsUserId})：自动签到{(account.AutoSignEnabled ? "开启" : "关闭")}"));
        return await Task.FromResult(response);
    }

    public CommandResponse BuildBindResult(CommandContext context, KuroBindResult result)
    {
        var response = CommandResponses.Ok(CommandResponseDataKind.KuroBindResult, context);
        response.KuroBindResult = new KuroBindResultData
        {
            Account = ToItem(result.Account, notificationEnabled: false),
            UpdatedExisting = result.UpdatedExisting
        };
        response.Message = result.UpdatedExisting ? "库街区账号已更新" : "库街区账号绑定成功";
        return response;
    }

    public CommandResponse BuildBbsSignResult(
        CommandContext context,
        KuroBbsSignResult result,
        bool autoSign = false)
    {
        var response = CommandResponses.Ok(CommandResponseDataKind.KuroBbsSignResult, context);
        response.KuroBbsSignResult = new KuroBbsSignResultData
        {
            Account = ToItem(result.Account, notificationEnabled: false),
            AutoSign = autoSign,
            OccurredAtUnixSeconds = timeProvider.GetUtcNow().ToUnixTimeSeconds()
        };
        response.KuroBbsSignResult.Progress.AddRange(result.Progress.Select(item => new KuroBbsTaskProgressItem
        {
            Remark = item.Remark,
            CompleteTimes = item.CompleteTimes,
            NeedActionTimes = item.NeedActionTimes,
            GainGold = item.GainGold,
            Finished = item.Finished
        }));
        response.KuroBbsSignResult.Lines.AddRange(result.Lines);
        response.Message = string.Join('\n', result.Lines);
        return response;
    }

    public CommandResponse BuildGameSignResult(
        CommandContext context,
        KuroGameSignResult result,
        bool autoSign = false)
    {
        var response = CommandResponses.Ok(CommandResponseDataKind.KuroGameSignResult, context);
        response.KuroGameSignResult = new KuroGameSignResultData
        {
            Account = ToItem(result.Account, notificationEnabled: false),
            AutoSign = autoSign,
            OccurredAtUnixSeconds = timeProvider.GetUtcNow().ToUnixTimeSeconds()
        };
        response.KuroGameSignResult.Lines.AddRange(result.Lines);
        response.Message = string.Join('\n', result.Lines);
        return response;
    }

    public async Task<CommandResponse> BuildBbsSignSelectionAsync(
        CommandContext context,
        IReadOnlyList<KuroAccount> accounts,
        IReadOnlyList<string> actions,
        CancellationToken cancellationToken = default)
    {
        var response = CommandResponses.Text("请选择要执行社区签到的库街区账号：", context);
        foreach (var account in accounts)
        {
            response.ButtonRows.Add(new ResponseButtonRow
            {
                Buttons =
                {
                    new ResponseButton
                    {
                        Text = $"{account.DisplayName} #{account.Id}",
                        Payload = await callbackStore.PutAsync(
                            "kuro-bbs-sign-select",
                            context.Identity.CoreUserId,
                            context.Request.ChatId,
                            context.Request.UserId,
                            new KuroBbsSignCallbackData(account.Id, actions.ToArray()),
                            cancellationToken: cancellationToken)
                    }
                }
            });
        }

        return response;
    }

    public async Task<CommandResponse> BuildGameSignSelectionAsync(
        CommandContext context,
        IReadOnlyList<KuroAccount> accounts,
        IReadOnlyList<long> gameIds,
        CancellationToken cancellationToken = default)
    {
        var response = CommandResponses.Text("请选择要执行游戏签到的库街区账号：", context);
        foreach (var account in accounts)
        {
            response.ButtonRows.Add(new ResponseButtonRow
            {
                Buttons =
                {
                    new ResponseButton
                    {
                        Text = $"{account.DisplayName} #{account.Id}",
                        Payload = await callbackStore.PutAsync(
                            "kuro-game-sign-select",
                            context.Identity.CoreUserId,
                            context.Request.ChatId,
                            context.Request.UserId,
                            new KuroGameSignCallbackData(account.Id, gameIds.ToArray()),
                            cancellationToken: cancellationToken)
                    }
                }
            });
        }

        return response;
    }

    public async Task<CommandResponse> BuildDeletePanelAsync(
        CommandContext context,
        IReadOnlyList<KuroAccount> accounts,
        CancellationToken cancellationToken = default)
    {
        var response = CommandResponses.Text("请选择要删除的库街区账号：", context);
        foreach (var account in accounts)
        {
            response.ButtonRows.Add(new ResponseButtonRow
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
        if (!string.IsNullOrWhiteSpace(editMessageId))
        {
            response.EditMessageId = editMessageId;
            response.ReplyToMessageId = string.Empty;
        }

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
        response.ButtonRows.Add(new ResponseButtonRow
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
        response.ButtonRows.Add(await BackRowAsync(context, "返回账号列表", "kuro-autosign-root-menu", new KuroAutoSignMenuCallbackData(0, "root"), cancellationToken));
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
        if (!string.IsNullOrWhiteSpace(editMessageId))
        {
            response.EditMessageId = editMessageId;
            response.ReplyToMessageId = string.Empty;
        }

        response.ButtonRows.Add(new ResponseButtonRow
        {
            Buttons =
            {
                await TaskButtonAsync(context, account, KuroBbsTaskFlags.SignIn, "签到", cancellationToken),
                await TaskButtonAsync(context, account, KuroBbsTaskFlags.ViewPosts, "浏览", cancellationToken)
            }
        });
        response.ButtonRows.Add(new ResponseButtonRow
        {
            Buttons =
            {
                await TaskButtonAsync(context, account, KuroBbsTaskFlags.LikePosts, "点赞", cancellationToken),
                await TaskButtonAsync(context, account, KuroBbsTaskFlags.SharePosts, "分享", cancellationToken)
            }
        });
        response.ButtonRows.Add(new ResponseButtonRow
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
        response.ButtonRows.Add(await BackRowAsync(context, "返回", "kuro-autosign-account-menu", new KuroAutoSignMenuCallbackData(account.Id, "account"), cancellationToken));
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
        if (!string.IsNullOrWhiteSpace(editMessageId))
        {
            response.EditMessageId = editMessageId;
            response.ReplyToMessageId = string.Empty;
        }

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
        response.ButtonRows.Add(new ResponseButtonRow
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
        response.ButtonRows.Add(await BackRowAsync(context, "返回", "kuro-autosign-account-menu", new KuroAutoSignMenuCallbackData(account.Id, "account"), cancellationToken));
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
                response.ButtonRows.Add(row);
                row = new ResponseButtonRow();
            }
        }

        if (row.Buttons.Count > 0)
        {
            response.ButtonRows.Add(row);
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
                response.ButtonRows.Add(row);
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
            response.ButtonRows.Add(row);
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
        var response = CommandResponses.Ok(CommandResponseDataKind.NotifyTypePanel, context);
        response.NotifyTypePanel = new NotifyTypePanelData();
        response.NotifyTypePanel.Items.Add(new NotifyTypeItem
        {
            Type = NotificationTypes.KuroAutoSign,
            DisplayName = NotificationTypes.KuroAutoSignDisplayName,
            Enabled = enabled.Count > 0
        });
        response.Message = "[消息订阅管理]\n当前已启用: " + (enabled.Count > 0 ? $"`{NotificationTypes.KuroAutoSignDisplayName}`" : "无");
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
        var response = CommandResponses.Ok(CommandResponseDataKind.NotifyAccountPanel, context);
        response.NotifyAccountPanel = new NotifyAccountPanelData
        {
            Type = NotificationTypes.KuroAutoSign,
            DisplayName = NotificationTypes.KuroAutoSignDisplayName
        };
        response.NotifyAccountPanel.KuroAccounts.AddRange(accounts.Select(account => ToItem(account, enabled.Contains(account.Id))));
        response.Message = $"[{NotificationTypes.KuroAutoSignDisplayName}]\n当前已启用: " + FormatEnabledAccounts(accounts, enabled);
        if (!string.IsNullOrWhiteSpace(editMessageId))
        {
            response.EditMessageId = editMessageId;
            response.ReplyToMessageId = string.Empty;
        }

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

    public static KuroAccountItem ToItem(KuroAccount account, bool notificationEnabled)
    {
        var item = new KuroAccountItem
        {
            Id = account.Id,
            BbsUserId = account.BbsUserId,
            DisplayName = account.DisplayName,
            AutoSignEnabled = account.AutoSignEnabled,
            BbsTaskFlags = account.BbsTaskFlags,
            NotificationEnabled = notificationEnabled
        };
        item.Roles.AddRange(account.Roles
            .OrderBy(role => role.GameId)
            .ThenBy(role => role.RoleId)
            .Select(role => new KuroGameRoleItem
            {
                Id = role.Id,
                GameId = role.GameId,
                GameName = role.GameName,
                ServerId = role.ServerId,
                ServerName = role.ServerName,
                RoleId = role.RoleId,
                RoleName = role.RoleName,
                GameLevel = role.GameLevel,
                AutoSignEnabled = role.AutoSignEnabled
            }));
        return item;
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

    private static string FormatEnabledAccounts(IReadOnlyList<KuroAccount> accounts, IReadOnlySet<long> enabled)
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
