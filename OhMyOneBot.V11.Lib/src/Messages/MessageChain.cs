using OhMyOneBot.V11.Lib.Messages.Entity;

namespace OhMyOneBot.V11.Lib.Messages;

public class MessageChain
{
    public required string ChatId { get; init; }
    public required string SenderId { get; init; }
    public required string MessageId { get; init; }
    public List<MessageEntity> Entities { get; init; } = [];
}