namespace OhMyBot.Core.Admin;

public interface IAdminCommand
{
    AdminCommandDefinition Definition { get; }

    Task<AdminCommandResult> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default);
}
