using OhMyBot.Contracts.Grpc;

namespace OhMyBot.QQGateway;

// QQ 端响应渲染：Core 已产出最终纯文本内容（含编号菜单）。网关把每条消息发出去；
// 若带 MenuToken（说明是数字菜单），发出后需 BindQqMenu 把「消息 id -> 选项」绑定到 Core。
public sealed record QQRenderedMessage(string Text, string MenuToken);

public sealed class QQResponseRenderer
{
    public IReadOnlyList<QQRenderedMessage> Render(CommandResponse response)
    {
        if (response.PlatformResponseCase != CommandResponse.PlatformResponseOneofCase.Qq)
        {
            return [];
        }

        return response.Qq.Messages
            .Where(message => !string.IsNullOrWhiteSpace(message.Text))
            .Select(message => new QQRenderedMessage(message.Text, message.MenuToken))
            .ToArray();
    }
}
