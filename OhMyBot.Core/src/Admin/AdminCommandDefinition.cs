namespace OhMyBot.Core.Admin;

public sealed record AdminCommandDefinition(
    string Name,
    string Usage,
    string Description,
    IReadOnlyList<AdminCommandOptionDefinition> Options,
    IReadOnlyList<string> Examples);

public sealed record AdminCommandOptionDefinition(
    string Display,
    string Description);
