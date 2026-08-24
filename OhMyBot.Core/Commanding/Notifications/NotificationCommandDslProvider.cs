using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Commands;
using OhMyBot.Core.Commanding.Presentation;

namespace OhMyBot.Core.Commanding.Notifications;

public sealed class NotificationCommandDslProvider(
    CallbackActionStore callbackStore,
    PluginNotificationSourceRegistry? sourceRegistry = null) : IPlatformCommandDslProvider
{
    private readonly PluginNotificationSourceRegistry _sourceRegistry = sourceRegistry ?? new PluginNotificationSourceRegistry();

    public IEnumerable<CommandDslNode> GetNodes()
    {
        return
        [
            new CommandDslNode
            {
                Name = "notify",
                Description = "管理消息订阅",
                Usage = "/notify",
                RequiredPrivilege = UserPrivilege.VerifiedUser,
                SupportPlatforms = SupportedPlatforms.All,
                SupportChatTypes = SupportedChatTypes.Private,
                Handler = context => BuildRootAsync(context, null, context.CancellationToken)
            }
        ];
    }

    public async Task<CommandResponse> BuildRootAsync(
        CommandContext context,
        string? editMessageId,
        CancellationToken cancellationToken = default)
    {
        var sources = _sourceRegistry.Sources
            .Where(source => PluginNotificationSourceAccess.CanManage(source, context))
            .ToArray();
        var rootItems = BuildRootItems(sources);
        var enabledNames = new List<string>();
        foreach (var item in rootItems)
        {
            if (await HasEnabledSourceAsync(item.Sources, context, cancellationToken))
            {
                enabledNames.Add(item.DisplayName);
            }
        }

        var text = MarkdownV2.Escape("[消息订阅管理]") + "\n当前已启用：" +
            TextLayout.JoinOrEmpty(
                enabledNames.Select(MarkdownV2.CodeSpan),
                MarkdownV2.Escape("、"),
                "无");
        var response = CommandResponses.TelegramMarkdown(context.Identity, text, context.Request.MessageId)
            .AsTelegramEditIfSpecified(editMessageId);

        // ownerPluginId 必须是 "core"：插件热重载时按归属清理回调，漏了会把 Core 自己的按钮一起清掉。
        var panel = new PanelBuilder(callbackStore, context, ownerPluginId: "core");
        await panel.AddGridAsync(
            response,
            rootItems,
            columns: 2,
            item => item.CategoryId is null ? "notify-type-select" : "notify-category-select",
            item => item.DisplayName,
            item => item.CategoryId is null
                ? new NotificationTypeCallbackData(item.Sources[0].Type)
                : new NotificationCategoryCallbackData(item.CategoryId),
            cancellationToken);

        return response;
    }

    public async Task<CommandResponse> BuildCategoryAsync(
        CommandContext context,
        string categoryId,
        string? editMessageId,
        CancellationToken cancellationToken = default)
    {
        var sources = _sourceRegistry.Sources
            .Where(source => string.Equals(source.Category?.Id, categoryId, StringComparison.OrdinalIgnoreCase)
                && PluginNotificationSourceAccess.CanManage(source, context))
            .OrderBy(source => source.Order)
            .ThenBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sources.Length == 0)
        {
            return PluginCallbackResponses.Error(context.Identity, editMessageId ?? string.Empty, "当前账号无权管理这个通知分类。");
        }

        var category = sources[0].Category!;
        var enabledNames = new List<string>();
        foreach (var source in sources)
        {
            if (await source.HasEnabledTargetsAsync(context, cancellationToken))
            {
                enabledNames.Add(source.DisplayName);
            }
        }

        var text = MarkdownV2.Escape($"[消息订阅 · {category.DisplayName}]") + "\n当前已启用：" +
            TextLayout.JoinOrEmpty(
                enabledNames.Select(MarkdownV2.CodeSpan),
                MarkdownV2.Escape("、"),
                "无");
        var response = CommandResponses.TelegramMarkdown(context.Identity, text, context.Request.MessageId)
            .AsTelegramEditIfSpecified(editMessageId);
        var panel = new PanelBuilder(callbackStore, context, ownerPluginId: "core");
        await panel.AddGridAsync(
            response,
            sources,
            columns: 2,
            "notify-type-select",
            source => source.DisplayName,
            source => new NotificationTypeCallbackData(source.Type),
            cancellationToken);
        return response.AddButtonRow(PanelBuilder.Row(
            await panel.ButtonAsync("notify-back", "返回", new NotifyBackCallbackData(), cancellationToken)));
    }

    private static IReadOnlyList<NotificationRootItem> BuildRootItems(IReadOnlyList<IPluginNotificationSource> sources)
    {
        var direct = sources
            .Where(source => source.Category is null)
            .Select(source => new NotificationRootItem(null, source.DisplayName, source.Order, [source]));
        var categories = sources
            .Where(source => source.Category is not null)
            .GroupBy(source => source.Category!.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var category = group.First().Category!;
                return new NotificationRootItem(category.Id, category.DisplayName, category.Order, group.ToArray());
            });
        return direct.Concat(categories)
            .OrderBy(item => item.Order)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<bool> HasEnabledSourceAsync(
        IReadOnlyList<IPluginNotificationSource> sources,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            if (await source.HasEnabledTargetsAsync(context, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record NotificationRootItem(
        string? CategoryId,
        string DisplayName,
        int Order,
        IReadOnlyList<IPluginNotificationSource> Sources);
}
