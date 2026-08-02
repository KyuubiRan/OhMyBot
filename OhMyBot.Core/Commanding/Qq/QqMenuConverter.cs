using System.Text;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Presentation;

namespace OhMyBot.Core.Commanding.Qq;

/// <summary>
/// 把「Telegram 形态」的交互响应（文本 + button_rows）在 gRPC 边界渲染成 QQ 编号菜单纯文本。
/// 每个按钮 → 一个编号项，编号 N 对应第 N 个按钮的 payload（回调 token）。选项 payload 存入
/// <see cref="QqMenuStore"/> 得到 menu_token 写进 QqMessage，供网关发出后 BindQqMenu 绑定到消息 id。
/// </summary>
public sealed class QqMenuConverter(QqMenuStore menuStore)
{
    /// <summary>
    /// 若 <paramref name="response"/> 带 Telegram 分支则转换为 QQ 分支（含编号菜单）；否则原样返回。
    /// </summary>
    public async Task<CommandResponse> ToQqAsync(
        CommandResponse response,
        BotChatType chatType,
        CancellationToken cancellationToken = default)
    {
        if (response.PlatformResponseCase != CommandResponse.PlatformResponseOneofCase.Telegram)
        {
            return response;
        }

        var qq = new QqResponse();
        foreach (var message in response.Telegram.Messages)
        {
            var text = MarkdownV2.ToPlain(message.Text);
            var payloads = message.ButtonRows
                .SelectMany(row => row.Buttons)
                .Select(button => button.Payload)
                .Where(payload => !string.IsNullOrEmpty(payload))
                .ToArray();
            var labels = message.ButtonRows
                .SelectMany(row => row.Buttons)
                .Where(button => !string.IsNullOrEmpty(button.Payload))
                .Select(button => button.Text)
                .ToArray();

            if (payloads.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    qq.Messages.Add(new QqMessage { Text = text });
                }

                continue;
            }

            var token = await menuStore.PutTokenAsync(payloads, cancellationToken);
            qq.Messages.Add(new QqMessage
            {
                Text = RenderMenu(text, labels, chatType),
                MenuToken = token
            });
        }

        // 赋值 Qq 会自动清空 oneof 里的 Telegram 分支。
        response.Qq = qq;

        // Telegram 用 toast 展示的提示（如「请至少勾选一个游戏」）在 QQ 无对应形态，
        // 成功响应（Code==0）时并入首条消息文本；错误响应的提示已在正文里，跳过以免重复。
        if (response.Code == 0
            && !string.IsNullOrWhiteSpace(response.CallbackAnswerText)
            && qq.Messages.Count > 0)
        {
            var first = qq.Messages[0];
            first.Text = string.IsNullOrEmpty(first.Text)
                ? response.CallbackAnswerText
                : $"{response.CallbackAnswerText}\n\n{first.Text}";
        }

        return response;
    }

    private static string RenderMenu(string text, IReadOnlyList<string> labels, BotChatType chatType)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(text))
        {
            builder.Append(text.TrimEnd()).Append('\n').Append('\n');
        }

        for (var index = 0; index < labels.Count; index++)
        {
            builder.Append(index + 1).Append(". ").Append(labels[index]).Append('\n');
        }

        builder.Append('\n').Append(chatType == BotChatType.Private
            ? "回复本消息序号，或直接发送序号进行选择。"
            : "回复本消息并发送序号进行选择。");
        return builder.ToString();
    }
}
