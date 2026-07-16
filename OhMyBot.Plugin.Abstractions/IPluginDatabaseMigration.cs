namespace OhMyBot.Plugin.Abstractions;

public interface IPluginDatabaseMigration
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
}
