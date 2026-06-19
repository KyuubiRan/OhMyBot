namespace OhMyBot.Core.Admin;

public sealed class AdminCommandCatalog
{
    private readonly IReadOnlyList<IAdminCommand> _commandList;
    private readonly Dictionary<string, IAdminCommand> _commands;

    public AdminCommandCatalog(IEnumerable<IAdminCommand> commands)
    {
        _commandList = commands.ToArray();
        _commands = BuildCommandMap(_commandList);
    }

    public IReadOnlyList<AdminCommandDefinition> Definitions => _commandList
        .Select(command => command.Definition)
        .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public bool TryGet(string commandName, out IAdminCommand command)
    {
        return _commands.TryGetValue(commandName, out command!);
    }

    private static Dictionary<string, IAdminCommand> BuildCommandMap(IEnumerable<IAdminCommand> commands)
    {
        var map = new Dictionary<string, IAdminCommand>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands)
        {
            map[command.Definition.Name] = command;
            foreach (var alias in command.Definition.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)))
            {
                map[alias.Trim()] = command;
            }
        }

        return map;
    }
}
