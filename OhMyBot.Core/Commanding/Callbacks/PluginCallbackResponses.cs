using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Infrastructure.Identity;

namespace OhMyBot.Core.Commanding.Callbacks;

public static class PluginCallbackResponses
{
    public static CommandResponse Error(
        ResolvedIdentity identity,
        string editMessageId,
        string message,
        string errorCode = "CallbackRejected")
    {
        return new CommandResponse
        {
            Code = 1,
            ErrorCode = errorCode,
            CallbackAnswerText = message,
            CallbackAnswerAlert = false,
            Context = ToContext(identity),
            Telegram = new TelegramResponse
            {
                Messages =
                {
                    new TelegramMessage
                    {
                        Text = $"错误：{message}（{errorCode}）",
                        ParseMode = TelegramParseMode.None,
                        EditMessageId = editMessageId
                    }
                }
            }
        };
    }

    public static CommandResponse Noop(ResolvedIdentity identity)
    {
        return new CommandResponse
        {
            Code = 0,
            Context = ToContext(identity),
            Telegram = new TelegramResponse()
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
