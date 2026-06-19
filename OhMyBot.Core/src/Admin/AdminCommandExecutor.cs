namespace OhMyBot.Core.Admin;

public sealed class AdminCommandExecutor(AdminCommandCatalog commandCatalog)
{
    private readonly AdminCommandHelpRenderer _helpRenderer = new(commandCatalog.Definitions);

    public async Task<AdminCommandResult> ExecuteAsync(string commandLine, CancellationToken cancellationToken = default)
    {
        var tokens = AdminCommandParser.Tokenize(commandLine);
        if (tokens.Count == 0)
        {
            return AdminCommandResult.Ok(string.Empty);
        }

        var commandName = tokens[0];
        if (string.Equals(commandName, "help", StringComparison.OrdinalIgnoreCase))
        {
            return AdminCommandResult.Ok(ExecuteHelp(tokens));
        }

        if (!commandCatalog.TryGet(commandName, out var command))
        {
            return AdminCommandResult.Error($"Unknown command '{commandName}'. Type 'help' for available commands.");
        }

        return await command.ExecuteAsync(tokens.Skip(1).ToArray(), cancellationToken);
    }

    private string ExecuteHelp(IReadOnlyList<string> tokens)
    {
        return tokens.Count > 1
            ? _helpRenderer.RenderCommandHelp(tokens[1])
            : _helpRenderer.RenderCommandList();
    }
}
