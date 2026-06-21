using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Commands;

public delegate Task<CommandResponse> CommandDslHandler(CommandContext context);

public sealed class CommandDslNode
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string Usage { get; init; }

    public IReadOnlyList<string> Aliases { get; init; } = [];

    public UserPrivilege RequiredPrivilege { get; init; } = UserPrivilege.User;

    public SupportedPlatforms SupportPlatforms { get; init; } = SupportedPlatforms.All;

    public SupportedChatTypes SupportChatTypes { get; init; } = SupportedChatTypes.All;

    public bool Enabled { get; init; } = true;

    public IReadOnlyList<CommandDslNode> Children { get; init; } = [];

    public CommandDslHandler? Handler { get; init; }
}

