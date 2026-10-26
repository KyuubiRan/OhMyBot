using Cronos;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Presentation;
using OhMyBot.Core.Infrastructure.ScheduledTasks;

namespace OhMyBot.Core.Commanding.Commands;

public sealed class TaskCommandDslProvider(
    ManagedTaskRegistry registry,
    ManagedTaskControlService control,
    CallbackActionStore callbackStore,
    TimeProvider timeProvider) : IPlatformCommandDslProvider
{
    public const string CallbackActionType = "task-manage";
    private const int PageSize = 8;
    private const int ErrorPageSize = 1800;

    public IEnumerable<CommandDslNode> GetNodes()
    {
        yield return new CommandDslNode
        {
            Name = "task",
            Aliases = ["tasks", "taskctl"],
            Description = "查看和管理插件定时任务（owner）",
            Usage = "/task [list|status|errors|execute|cancel|enable|disable] [任务名]",
            RequiredPrivilege = UserPrivilege.Owner,
            SupportChatTypes = SupportedChatTypes.Private,
            Handler = context => BuildListAsync(context),
            Children = new[] { "list", "status", "errors", "execute", "cancel", "enable", "disable" }
                .Select(operation => new CommandDslNode
                {
                    Name = operation,
                    Description = operation switch
                    {
                        "list" => "列出定时任务",
                        "status" => "查看任务详情",
                        "errors" => "查看最近报错详情",
                        "execute" => "执行任务一次",
                        "cancel" => "取消本次执行",
                        "enable" => "启用后续定时执行",
                        _ => "禁用后续定时执行"
                    },
                    Usage = operation == "list" ? "/task list" : $"/task {operation} <任务名>",
                    RequiredPrivilege = UserPrivilege.Owner,
                    SupportChatTypes = SupportedChatTypes.Private,
                    Handler = context => ExecuteCommandAsync(context, operation)
                }).ToArray()
        };
    }

    private Task<CommandResponse> ExecuteCommandAsync(CommandContext context, string operation)
    {
        if (operation == "list") return BuildListAsync(context);
        if (context.Request.Args.Count == 0)
            return Task.FromResult(CommandResponses.Error("TaskNameMissing", "请提供任务名，或使用 /task 打开任务列表。", context));
        if (!registry.TryGetRegistration(context.Request.Args[0], out var task))
            return Task.FromResult(CommandResponses.Error("TaskNotFound", "未找到该任务，插件可能已卸载。", context));
        return ExecuteAsync(context, new TaskManagementCallbackData(operation == "status" ? "detail" : operation,
            task.Task.Name, task.RegistrationId), null);
    }

    public Task<CommandResponse> ExecuteCallbackAsync(CommandContext context, CallbackAction action, string editMessageId)
    {
        var data = CallbackActionStore.ReadData<TaskManagementCallbackData>(action);
        return data is null
            ? Task.FromResult(CommandResponses.Error("TaskCallbackInvalid", "任务按钮数据无效。", context))
            : ExecuteAsync(context, data, editMessageId);
    }

    private async Task<CommandResponse> ExecuteAsync(CommandContext context, TaskManagementCallbackData data, string? editMessageId)
    {
        if (!CanManage(context)) return Denied(context);
        if (data.View == "list") return await BuildListAsync(context, data.Page, editMessageId);
        if (data.TaskName is null || !registry.TryGetRegistration(data.TaskName, out var registration)
            || registration.RegistrationId != data.RegistrationId)
        {
            return await BuildListAsync(context, data.Page, editMessageId, "任务已卸载或重新加载，请重新选择。");
        }

        var task = registration.Task;
        string? notice = null;
        switch (data.View)
        {
            case "execute":
                notice = control.TryStart(registration) ? "已开始执行一次。" : "任务已在运行，或插件正在停止。";
                break;
            case "cancel":
                notice = registry.TryControl(registration, current => current.Cancel())
                    ? "已请求取消本次执行，等待任务响应取消。" : "任务当前没有运行，或插件已卸载。";
                break;
            case "enable":
                if (!registry.TryControl(registration, current => { current.Enabled = true; return true; }))
                    return await BuildListAsync(context, data.Page, editMessageId, "任务已卸载或重新加载，请重新选择。");
                notice = "已启用后续定时调度。";
                break;
            case "disable":
                if (!registry.TryControl(registration, current => { current.Enabled = false; return true; }))
                    return await BuildListAsync(context, data.Page, editMessageId, "任务已卸载或重新加载，请重新选择。");
                notice = "已禁用后续定时调度；当前执行会继续，可单独取消。";
                break;
            case "errors":
                return await BuildErrorAsync(context, registration, data, editMessageId);
            case "detail":
                break;
            default:
                return await BuildListAsync(context, data.Page, editMessageId, "未知任务操作，请重新选择。");
        }

        return await BuildDetailAsync(context, registration, data.Page, editMessageId, notice);
    }

    public async Task<CommandResponse> BuildListAsync(CommandContext context, int page = 0, string? editMessageId = null, string? notice = null)
    {
        if (!CanManage(context)) return Denied(context);
        var all = registry.Registrations;
        var pages = Math.Max(1, (all.Count + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 0, pages - 1);
        var items = all.Skip(page * PageSize).Take(PageSize).ToArray();
        var lines = new List<string> { $"[定时任务] 共 {all.Count} 项 · 第 {page + 1}/{pages} 页" };
        if (notice is not null) lines.Add(notice);
        if (items.Length == 0) lines.Add("当前没有已注册的任务。");
        foreach (var registration in items)
        {
            var task = registration.Task;
            var status = task.Status;
            lines.Add($"\n{task.Name} · {Owner(registration)}\n{(task.Enabled ? "已启用" : "已禁用")} / {Outcome(status)}\n最后执行：{Time(status.LastStartedAt)}" +
                (status.LastFailedAt is not null || status.LastError is not null ? "\n最近有报错，可进入详情查看。" : ""));
        }

        var response = Plain(context, string.Join('\n', lines), editMessageId);
        var panel = Panel(context);
        await panel.AddGridAsync(response, items, 1, CallbackActionType,
            item => Trim(item.Task.Name, 55),
            item => new TaskManagementCallbackData("detail", item.Task.Name, item.RegistrationId, page), context.CancellationToken);
        await panel.AddPagerAsync(response, CallbackActionType, page, pages,
            p => new TaskManagementCallbackData("list", Page: p), "上一页", "下一页", context.CancellationToken);
        await panel.AddRowAsync(response, CallbackActionType, "刷新列表", new TaskManagementCallbackData("list", Page: page), context.CancellationToken);
        return response;
    }

    private async Task<CommandResponse> BuildDetailAsync(CommandContext context, ManagedTaskRegistration registration, int page,
        string? editMessageId, string? notice)
    {
        var task = registration.Task;
        var status = task.Status;
        var lines = new List<string>
        {
            "[任务详情]", $"名称：{task.Name}", $"所属插件：{Owner(registration)}", $"说明：{Trim(task.Description, 300)}",
            $"调度：{(task.Enabled ? "已启用" : "已禁用")}", $"执行状态：{Outcome(status)}", $"Cron：{task.Cron}",
            $"调度时区：{TimeZoneInfo.Local.Id}", $"下次定时执行：{Next(task)}",
            $"最后开始：{Time(status.LastStartedAt)}", $"最后结束：{Time(status.LastFinishedAt)}",
            $"最后成功：{Time(status.LastSucceededAt)}", $"最后报错：{Time(status.LastFailedAt)}",
            $"本次/最近执行报错数：{status.FailureCount}", $"最近错误 ID：{status.LastErrorId ?? "无"}",
            "手动执行不受启用开关限制。禁用仅暂停后续调度。"
        };
        if (notice is not null) lines.Add("\n" + notice);
        if (task.StatePersistenceError is { } persistenceError) lines.Add("\n" + persistenceError);
        var response = Plain(context, string.Join('\n', lines), editMessageId);
        var panel = Panel(context);
        var data = new TaskManagementCallbackData("detail", task.Name, registration.RegistrationId, page);
        response.AddButtonRow(PanelBuilder.Row(
            await panel.ButtonAsync(CallbackActionType, task.IsRunning ? "取消本次执行" : "执行一次", data with { View = task.IsRunning ? "cancel" : "execute" }, context.CancellationToken),
            await panel.ButtonAsync(CallbackActionType, task.Enabled ? "禁用调度" : "启用调度", data with { View = task.Enabled ? "disable" : "enable" }, context.CancellationToken)));
        if (status.LastError is not null || !IsValidCron(task))
            await panel.AddRowAsync(response, CallbackActionType, "查看报错详情", data with { View = "errors" }, context.CancellationToken);
        response.AddButtonRow(PanelBuilder.Row(
            await panel.ButtonAsync(CallbackActionType, "刷新状态", data, context.CancellationToken),
            await panel.ButtonAsync(CallbackActionType, "返回列表", new TaskManagementCallbackData("list", Page: page), context.CancellationToken)));
        return response;
    }

    private async Task<CommandResponse> BuildErrorAsync(CommandContext context, ManagedTaskRegistration registration,
        TaskManagementCallbackData data, string? editMessageId)
    {
        var status = registration.Task.Status;
        var details = status.LastErrorDetails ?? status.LastError ?? (IsValidCron(registration.Task) ? "暂无报错记录。" : "Cron 表达式无效，任务无法自动调度。");
        var chunks = Split(ManagedTaskErrorRedactor.Redact(details));
        var page = Math.Clamp(data.ErrorPage, 0, chunks.Count - 1);
        var text = $"[任务报错详情] {registration.Task.Name}\n所属插件：{Owner(registration)}\n错误 ID：{status.LastErrorId ?? "无"}\n时间：{Time(status.LastFailedAt)}\n第 {page + 1}/{chunks.Count} 页\n\n{chunks[page]}";
        var response = Plain(context, text, editMessageId);
        var panel = Panel(context);
        await panel.AddPagerAsync(response, CallbackActionType, page, chunks.Count,
            p => data with { ErrorPage = p }, "上一页", "下一页", context.CancellationToken);
        await panel.AddRowAsync(response, CallbackActionType, "返回任务详情", data with { View = "detail", ErrorPage = 0 }, context.CancellationToken);
        return response;
    }

    private PanelBuilder Panel(CommandContext context) => new(callbackStore, context, ownerPluginId: "core");
    private static bool CanManage(CommandContext context)
        => context.Identity.Privilege == UserPrivilege.Owner && context.Request.ChatType == BotChatType.Private;
    private static CommandResponse Denied(CommandContext context)
        => CommandResponses.Error("TaskManagementForbidden", "仅 bot owner 可以在私聊中管理定时任务。", context);
    private static CommandResponse Plain(CommandContext context, string text, string? editMessageId)
        => CommandResponses.TelegramPlain(context.Identity, text, context.Request.MessageId, editMessageId);
    private static string Owner(ManagedTaskRegistration item) => item.OwnerId == "core" ? "Core" : item.OwnerId;
    private static string Time(DateTimeOffset? value) => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "无记录";
    private static string Trim(string value, int limit) => value.Length > limit ? value[..limit] + "…" : value;
    private static string Outcome(ManagedTaskStatus status) => status.Outcome switch
    {
        ManagedTaskOutcome.Running => status.CancellationRequested ? "正在取消" : "执行中",
        ManagedTaskOutcome.Succeeded => "成功",
        ManagedTaskOutcome.Failed => "失败（含部分失败）",
        ManagedTaskOutcome.Canceled => "已取消",
        ManagedTaskOutcome.Interrupted => "上次执行中断（进程退出，完成状态未知）",
        _ => "尚未执行"
    };
    private static bool IsValidCron(IManagedTask task)
    {
        try { CronExpression.Parse(task.Cron); return true; }
        catch (CronFormatException) { return false; }
    }
    private string Next(IManagedTask task)
    {
        if (!task.Enabled) return "已禁用";
        try { return Time(CronExpression.Parse(task.Cron).GetNextOccurrence(timeProvider.GetUtcNow(), TimeZoneInfo.Local)); }
        catch (CronFormatException) { return "Cron 表达式无效"; }
    }
    private static IReadOnlyList<string> Split(string value)
    {
        var chunks = new List<string>();
        for (var offset = 0; offset < value.Length;)
        {
            var length = Math.Min(ErrorPageSize, value.Length - offset);
            if (offset + length < value.Length && char.IsHighSurrogate(value[offset + length - 1])) length--;
            chunks.Add(value.Substring(offset, length));
            offset += length;
        }
        return chunks.Count == 0 ? ["暂无报错记录。"] : chunks;
    }
}

public sealed record TaskManagementCallbackData(string View, string? TaskName = null, string? RegistrationId = null, int Page = 0, int ErrorPage = 0);
