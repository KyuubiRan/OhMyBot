namespace OhMyBot.Core.Admin;

public sealed class AdminCommandHelpRenderer(IReadOnlyList<AdminCommandDefinition> commands)
{
    public string RenderCommandList()
    {
        return string.Join(
            Environment.NewLine,
            ["Available commands:", .. commands.Select(RenderCommandListItem), "  help     Show available commands.", "  exit     Stop OhMyBot Core. aliases: quit"]);
    }

    public string RenderCommandHelp(string commandName)
    {
        var command = commands.FirstOrDefault(item => string.Equals(item.Name, commandName, StringComparison.OrdinalIgnoreCase));
        if (command is null)
        {
            return $"Unknown command '{commandName}'. Type 'help' for available commands.";
        }

        var lines = new List<string>
        {
            $"usage: {command.Usage}",
            string.Empty,
            command.Description
        };

        if (command.Aliases.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"aliases: {string.Join(", ", command.Aliases)}");
        }

        if (command.Options.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("options:");
            var optionWidth = command.Options.Max(option => option.Display.Length);
            lines.AddRange(command.Options.Select(option => $"  {option.Display.PadRight(optionWidth)}  {option.Description}"));
        }

        if (command.Examples.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("examples:");
            lines.AddRange(command.Examples.Select(example => $"  {example}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string RenderCommandListItem(AdminCommandDefinition command)
    {
        var aliases = command.Aliases.Count > 0
            ? $" aliases: {string.Join(", ", command.Aliases)}"
            : string.Empty;

        return $"  {command.Name.PadRight(8)} {command.Description}{aliases}";
    }
}
