using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Commands;

public static class CommandResponses
{
    public static CommandResponse Text(string text, string? replyToMessageId = null)
    {
        var response = new CommandResponse { ReplyToMessageId = replyToMessageId ?? string.Empty };
        response.Messages.Add(new ResponseMessage { Text = text });
        return response;
    }

    public static CommandResponse Error(string code, string message, string? replyToMessageId = null)
    {
        return new CommandResponse
        {
            ReplyToMessageId = replyToMessageId ?? string.Empty,
            Error = new CommandError
            {
                Code = code,
                Message = message
            }
        };
    }
}
