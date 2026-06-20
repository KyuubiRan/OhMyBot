namespace OhMyBot.Core.Commands;

public sealed class CoreCommandCatalog(IEnumerable<ICoreCommand> commands)
{
    public IEnumerable<CommandRegistration> CreateRegistrations()
    {
        return commands.Select(command => new CommandRegistration(
            command.Name,
            command.Description,
            command.Usage,
            command.RequiredPrivilege,
            command.SupportPlatforms,
            command.ExecuteAsync,
            command.Enabled,
            command.Aliases,
            command.SupportChatTypes));
    }
}
