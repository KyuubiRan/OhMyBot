using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OhMyBot.Plugin.Abstractions;

namespace OhMyBot.Core.Infrastructure.Plugins;

public sealed record EfBaselineOptions(
    string PluginId,
    string? RelationTable = null,
    string RelationCoreUserIdColumn = "CoreUserId");

/// <summary>
/// Applies the plugin's EF migrations, then registers its CoreUserId-owning table
/// so account merges can move rows even while the plugin is offline.
/// </summary>
public sealed class EfBaselineMigration<TContext>(
    IServiceScopeFactory scopeFactory,
    EfBaselineOptions options) : IPluginDatabaseMigration
    where TContext : DbContext
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TContext>();
        await EfBaselineAdopter.AdoptOrMigrateAsync(dbContext, options, cancellationToken);

        if (!string.IsNullOrWhiteSpace(options.RelationTable))
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "PluginOwnedRelations"
                    ("PluginId", "SchemaName", "TableName", "CoreUserIdColumn")
                VALUES
                    ({{options.PluginId}}, 'public', {{options.RelationTable}}, {{options.RelationCoreUserIdColumn}})
                ON CONFLICT ("SchemaName", "TableName", "CoreUserIdColumn")
                DO UPDATE SET "PluginId" = EXCLUDED."PluginId"
                """, cancellationToken);
        }
    }
}

public static class EfBaselineServiceCollectionExtensions
{
    public static IServiceCollection AddPluginEfBaseline<TContext>(
        this IServiceCollection services,
        EfBaselineOptions options)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton<IPluginDatabaseMigration>(provider =>
            new EfBaselineMigration<TContext>(
                provider.GetRequiredService<IServiceScopeFactory>(),
                options));
        return services;
    }
}

public static class EfBaselineAdopter
{
    public static async Task AdoptOrMigrateAsync(
        DbContext dbContext,
        EfBaselineOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(options);
        if (!dbContext.Database.IsRelational())
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
            return;
        }

        if (dbContext.Database.GetMigrations().Any())
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
            return;
        }

        throw new InvalidOperationException($"{options.PluginId} 没有可用的 EF migration。");
    }
}
