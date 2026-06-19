namespace OhMyBot.Core.Admin;

public sealed class AdminCommandHelpRenderer(IReadOnlyList<AdminCommandDefinition> commands)
{
    public string RenderCommandList()
    {
        return string.Join(
            Environment.NewLine,
            ["Available commands:", .. commands.Select(command => $"  {command.Name.PadRight(8)} {command.Description}"), "  help     Show available commands.", "  exit     Stop OhMyBot Core."]);
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
            command.Description,
            string.Empty,
            "options:"
        };

        var optionWidth = Math.Max(0, command.Options.Max(option => option.Display.Length));
        lines.AddRange(command.Options.Select(option => $"  {option.Display.PadRight(optionWidth)}  {option.Description}"));

        if (command.Examples.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("examples:");
            lines.AddRange(command.Examples.Select(example => $"  {example}"));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
