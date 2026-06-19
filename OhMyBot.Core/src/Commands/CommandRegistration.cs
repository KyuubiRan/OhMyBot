using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Commands;

public sealed class CommandRegistration(
    string name,
    string description,
    string usage,
    UserPrivilege requiredPrivilege,
    SupportedPlatforms supportPlatforms,
    Func<CommandContext, Task<CommandResponse>> executeAsync,
    bool enabled = true,
    IReadOnlyList<string>? aliases = null)
{
    public string Name { get; } = Normalize(name);

    public string Description { get; } = description;

    public string Usage { get; } = usage;

    public UserPrivilege RequiredPrivilege { get; } = requiredPrivilege;

    public SupportedPlatforms SupportPlatforms { get; } = supportPlatforms;

    public bool Enabled { get; } = enabled;

    public IReadOnlyList<string> Aliases { get; } = NormalizeAliases(name, aliases);

    public Func<CommandContext, Task<CommandResponse>> ExecuteAsync { get; } = executeAsync;

    public static string Normalize(string command)
    {
        return command.Trim().TrimStart('/').ToLowerInvariant();
    }

    private static IReadOnlyList<string> NormalizeAliases(string name, IReadOnlyList<string>? aliases)
    {
        if (aliases is null || aliases.Count == 0)
        {
            return [];
        }

        var normalizedName = Normalize(name);
        return aliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(Normalize)
            .Where(alias => alias.Length > 0 && !string.Equals(alias, normalizedName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
