using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Commands;

public sealed class CommandRegistration(
    string name,
    string description,
    string usage,
    UserPrivilege requiredPrivilege,
    SupportedPlatforms supportPlatforms,
    Func<CommandContext, Task<CommandResponse>> executeAsync,
    bool enabled = true)
{
    public string Name { get; } = Normalize(name);

    public string Description { get; } = description;

    public string Usage { get; } = usage;

    public UserPrivilege RequiredPrivilege { get; } = requiredPrivilege;

    public SupportedPlatforms SupportPlatforms { get; } = supportPlatforms;

    public bool Enabled { get; } = enabled;

    public Func<CommandContext, Task<CommandResponse>> ExecuteAsync { get; } = executeAsync;

    public static string Normalize(string command)
    {
        return command.Trim().TrimStart('/').ToLowerInvariant();
    }
}
