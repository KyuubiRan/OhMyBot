using OhMyBot.Contracts.Grpc;

namespace OhMyBot.TelegramGateway.Rendering;

public sealed class NotifyTelegramRenderer : ITelegramCommandResultRenderer
{
    public bool CanRender(CommandResponse response)
    {
        return response.Code == 0
            && response.DataKind is CommandResponseDataKind.NotifyTypePanel
                or CommandResponseDataKind.NotifyAccountPanel;
    }

    public IReadOnlyList<TelegramOutgoingMessage> Render(CommandResponse response)
    {
        return response.DataKind switch
        {
            CommandResponseDataKind.NotifyTypePanel => [TelegramTextMessage.Markdown(RenderTypePanel(response.NotifyTypePanel))],
            CommandResponseDataKind.NotifyAccountPanel => [TelegramTextMessage.Markdown(RenderAccountPanel(response.NotifyAccountPanel))],
            _ => []
        };
    }

    private static string RenderTypePanel(NotifyTypePanelData data)
    {
        var enabled = data.Items.Where(type => type.Enabled).Select(type => $"`{AiRouterTelegramRenderer.Code(type.DisplayName)}`").ToArray();
        return string.Join('\n',
            AiRouterTelegramRenderer.Escape("[消息订阅管理]"),
            AiRouterTelegramRenderer.Escape("当前已启用：") + (enabled.Length == 0 ? AiRouterTelegramRenderer.Escape("无") : string.Join(AiRouterTelegramRenderer.Escape("、"), enabled)));
    }

    private static string RenderAccountPanel(NotifyAccountPanelData data)
    {
        var enabled = data.KuroAccounts.Count > 0
            ? data.KuroAccounts
                .Where(account => account.NotificationEnabled)
                .Select(account => $"`{AiRouterTelegramRenderer.Code(account.DisplayName)}`")
                .ToArray()
            : data.Accounts
                .Where(account => account.NotificationEnabled)
                .Select(account => $"`{AiRouterTelegramRenderer.Code(account.DisplayName)}`")
                .ToArray();
        return string.Join('\n',
            AiRouterTelegramRenderer.Escape($"[{data.DisplayName}]"),
            AiRouterTelegramRenderer.Escape("当前已启用：") + (enabled.Length == 0 ? AiRouterTelegramRenderer.Escape("无") : string.Join(AiRouterTelegramRenderer.Escape("、"), enabled)));
    }
}
