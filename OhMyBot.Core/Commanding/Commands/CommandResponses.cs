using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Presentation;
using OhMyBot.Core.Infrastructure.Identity;

namespace OhMyBot.Core.Commanding.Commands;

/// <summary>
/// 平台感知的响应工厂。Core 直接产出各平台的最终内容：Telegram 走 MarkdownV2 + 按钮，
/// QQ 走纯文本。网关只负责把 <see cref="CommandResponse"/> 的对应分支发出去。
/// </summary>
public static class CommandResponses
{
    // ---- 通用文本：按调用方平台自动渲染 ----

    public static CommandResponse Text(string text, CommandContext context)
        => Text(text, context.Identity, context.Request.MessageId);

    public static CommandResponse Text(string text, ResolvedIdentity identity, string? replyToMessageId = null)
    {
        var response = Envelope(identity);
        if (identity.Platform == BotPlatform.Qq)
        {
            response.Qq = new QqResponse();
            var plain = MarkdownV2.StripMarks(text);
            if (!string.IsNullOrEmpty(plain))
            {
                response.Qq.Messages.Add(new QqMessage { Text = plain });
            }
        }
        else
        {
            response.Telegram = new TelegramResponse();
            if (!string.IsNullOrEmpty(text))
            {
                response.Telegram.Messages.Add(new TelegramMessage
                {
                    Text = MarkdownV2.Inline(text),
                    ParseMode = TelegramParseMode.MarkdownV2,
                    ReplyToMessageId = replyToMessageId ?? string.Empty
                });
            }
        }

        return response;
    }

    // ---- 静默：空分支，网关不发送 ----

    public static CommandResponse Silent(CommandContext context) => Silent(context.Identity);

    public static CommandResponse Silent(ResolvedIdentity identity)
    {
        var response = Envelope(identity);
        if (identity.Platform == BotPlatform.Qq)
        {
            response.Qq = new QqResponse();
        }
        else
        {
            response.Telegram = new TelegramResponse();
        }

        return response;
    }

    // ---- 错误 ----

    public static CommandResponse Error(string errorCode, string message, CommandContext context, int code = 1)
        => Error(errorCode, message, context.Identity, context.Request.MessageId, code);

    public static CommandResponse Error(
        string errorCode,
        string message,
        ResolvedIdentity identity,
        string? replyToMessageId = null,
        int code = 1)
    {
        var response = Envelope(identity, code, errorCode);
        if (identity.Platform == BotPlatform.Qq)
        {
            response.Qq = new QqResponse();
            var text = string.IsNullOrWhiteSpace(message) ? errorCode : message;
            if (!string.IsNullOrWhiteSpace(text))
            {
                response.Qq.Messages.Add(new QqMessage { Text = text });
            }
        }
        else
        {
            response.Telegram = new TelegramResponse();
            response.Telegram.Messages.Add(new TelegramMessage
            {
                Text = $"错误：{message}（{errorCode}）",
                ParseMode = TelegramParseMode.None,
                ReplyToMessageId = replyToMessageId ?? string.Empty
            });
        }

        return response;
    }

    // ---- Telegram 富内容构造（文本须由调用方按 MarkdownV2 转义好） ----

    public static CommandResponse TelegramMarkdown(
        ResolvedIdentity identity,
        string markdown,
        string? replyToMessageId = null,
        string? editMessageId = null)
        => TelegramMessageResponse(identity, markdown, TelegramParseMode.MarkdownV2, replyToMessageId, editMessageId);

    public static CommandResponse TelegramPlain(
        ResolvedIdentity identity,
        string text,
        string? replyToMessageId = null,
        string? editMessageId = null)
        => TelegramMessageResponse(identity, text, TelegramParseMode.None, replyToMessageId, editMessageId);

    // ---- QQ 纯文本构造 ----

    public static CommandResponse Qq(ResolvedIdentity identity, params string[] texts)
    {
        var response = Envelope(identity);
        response.Qq = new QqResponse();
        foreach (var text in texts)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                response.Qq.Messages.Add(new QqMessage { Text = text });
            }
        }

        return response;
    }

    private static CommandResponse TelegramMessageResponse(
        ResolvedIdentity identity,
        string text,
        TelegramParseMode parseMode,
        string? replyToMessageId,
        string? editMessageId)
    {
        var message = new TelegramMessage
        {
            Text = text,
            ParseMode = parseMode,
            ReplyToMessageId = replyToMessageId ?? string.Empty
        };
        if (!string.IsNullOrWhiteSpace(editMessageId))
        {
            message.EditMessageId = editMessageId;
        }

        var response = Envelope(identity);
        response.Telegram = new TelegramResponse { Messages = { message } };
        return response;
    }

    private static CommandResponse Envelope(ResolvedIdentity identity, int code = 0, string errorCode = "")
    {
        return new CommandResponse
        {
            Code = code,
            ErrorCode = errorCode,
            Context = ToContext(identity)
        };
    }

    private static CommandResponseContext ToContext(ResolvedIdentity identity)
    {
        return new CommandResponseContext
        {
            CallerCoreUserId = identity.CoreUserId,
            CallerPrivilege = identity.Privilege,
            Platform = identity.Platform
        };
    }
}

/// <summary>Telegram 响应的便捷操作，主要给回调路径（编辑消息、追加按钮）用。</summary>
public static class TelegramResponseExtensions
{
    /// <summary>取第一条 Telegram 消息；presenter/回调据此追加按钮行。</summary>
    public static TelegramMessage FirstTelegram(this CommandResponse response)
    {
        EnsureTelegramShape(response);
        return response.Telegram.Messages[0];
    }

    /// <summary>把响应改为“编辑现有消息”：设置 edit id 并清掉回复目标。</summary>
    public static CommandResponse AsTelegramEdit(this CommandResponse response, string editMessageId)
    {
        if (response.Telegram is { Messages.Count: > 0 })
        {
            var message = response.Telegram.Messages[0];
            message.EditMessageId = editMessageId;
            message.ReplyToMessageId = string.Empty;
        }

        return response;
    }

    /// <summary>
    /// editMessageId 非空白时改为「编辑现有消息」，为空/空白时保持原样。
    ///
    /// 不要把这个判空并进 <see cref="AsTelegramEdit"/>：那个方法会无条件清掉 ReplyToMessageId，
    /// 而回调路径（editMessageId 为 ""）正依赖这个清空行为——加了守卫会让回调回复变成引用自身面板消息。
    /// </summary>
    public static CommandResponse AsTelegramEditIfSpecified(this CommandResponse response, string? editMessageId)
    {
        return string.IsNullOrWhiteSpace(editMessageId) ? response : response.AsTelegramEdit(editMessageId);
    }

    /// <summary>追加一行按钮到第一条 Telegram 消息。</summary>
    public static CommandResponse AddButtonRow(this CommandResponse response, ResponseButtonRow row)
    {
        EnsureTelegramShape(response);
        response.Telegram.Messages[0].ButtonRows.Add(row);
        return response;
    }

    /// <summary>
    /// 保证响应带 Telegram 分支且至少有一条消息，供追加按钮使用。
    /// QQ 交互命令会先经 <see cref="CommandResponses.Text"/> 产出 QQ 纯文本分支——这里把它就地迁移成
    /// Telegram 形态（保留纯文本），以便复用同一套按钮 builder；随后由 QQ 菜单转换器在 gRPC 边界转回编号菜单。
    /// 对已是 Telegram 形态的响应为无操作，不影响 Telegram 调用方。
    /// </summary>
    private static void EnsureTelegramShape(CommandResponse response)
    {
        if (response.PlatformResponseCase == CommandResponse.PlatformResponseOneofCase.Telegram
            && response.Telegram.Messages.Count > 0)
        {
            return;
        }

        var text = response.PlatformResponseCase == CommandResponse.PlatformResponseOneofCase.Qq
            ? response.Qq.Messages.FirstOrDefault()?.Text ?? string.Empty
            : string.Empty;

        // 赋值 Telegram 会自动清空 oneof 里的 Qq 分支。
        response.Telegram = new TelegramResponse
        {
            Messages = { new TelegramMessage { Text = text, ParseMode = TelegramParseMode.None } }
        };
    }
}
