using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Infrastructure.Identity;

namespace OhMyBot.Core.Commanding.Commands;

public static class CommandResponses
{
    public static CommandResponse Ok(
        CommandResponseDataKind dataKind,
        CommandContext context,
        string? message = null)
    {
        return Ok(dataKind, context.Identity, context.Request.MessageId, message);
    }

    public static CommandResponse Ok(
        CommandResponseDataKind dataKind,
        ResolvedIdentity identity,
        string? replyToMessageId = null,
        string? message = null)
    {
        return new CommandResponse
        {
            Code = 0,
            Message = message ?? string.Empty,
            DataKind = dataKind,
            Context = ToContext(identity),
            ReplyToMessageId = replyToMessageId ?? string.Empty
        };
    }

    public static CommandResponse Text(string text, CommandContext context)
    {
        var response = Ok(CommandResponseDataKind.Text, context, text);
        response.Text = new TextData { Text = text };
        return response;
    }

    public static CommandResponse Text(string text)
    {
        return new CommandResponse
        {
            Code = 0,
            DataKind = CommandResponseDataKind.Text,
            Message = text,
            Text = new TextData { Text = text }
        };
    }

    // 不产生任何回复：Code=0 且无内容，网关渲染为空消息直接跳过发送。
    // 用于未知子命令等场景，行为对齐未知主命令（静默不响应）。
    public static CommandResponse Silent(CommandContext context)
    {
        return new CommandResponse
        {
            Code = 0,
            Context = ToContext(context.Identity)
        };
    }

    public static CommandResponse Error(
        string errorCode,
        string message,
        CommandContext context,
        int code = 1)
    {
        return Error(errorCode, message, context.Identity, context.Request.MessageId, code);
    }

    public static CommandResponse Error(
        string errorCode,
        string message,
        ResolvedIdentity identity,
        string? replyToMessageId = null,
        int code = 1)
    {
        return new CommandResponse
        {
            Code = code,
            ErrorCode = errorCode,
            Message = message,
            Context = ToContext(identity),
            ReplyToMessageId = replyToMessageId ?? string.Empty
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
