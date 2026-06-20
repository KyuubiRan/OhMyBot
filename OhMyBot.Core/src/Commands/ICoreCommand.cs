using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Commands;

public interface ICoreCommand
{
    string Name { get; }

    string Description { get; }

    string Usage { get; }

    IReadOnlyList<string> Aliases { get; }

    UserPrivilege RequiredPrivilege { get; }

    SupportedPlatforms SupportPlatforms { get; }

    SupportedChatTypes SupportChatTypes { get; }

    bool Enabled { get; }

    Task<CommandResponse> ExecuteAsync(CommandContext context);
}
