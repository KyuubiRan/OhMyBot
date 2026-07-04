using OhMyBot.Contracts.Grpc;

namespace OhMyBot.QQGateway;

// QQ 端响应渲染：Core 已产出最终纯文本内容（无 Markdown、无按钮）。网关只把每条 QqMessage 发出去。
public sealed class QQResponseRenderer
{
    public IReadOnlyList<string> Render(CommandResponse response)
    {
        if (response.PlatformResponseCase != CommandResponse.PlatformResponseOneofCase.Qq)
        {
            return [];
        }

        return response.Qq.Messages
            .Select(message => message.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
    }
}
