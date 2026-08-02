using Microsoft.EntityFrameworkCore;
using Npgsql;
using OhMyBot.Core.Infrastructure.Data;
using OhMyBot.Core.Infrastructure.Plugins;

namespace OhMyBot.Tests;

[TestClass]
public sealed class PostgresMigrationTests
{
    [TestMethod]
    public async Task FreshDatabaseCreatesIndependentCoreSchema()
    {
        var connectionString = Environment.GetEnvironmentVariable("OHMYBOT_TEST_POSTGRES_FRESH");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("Set OHMYBOT_TEST_POSTGRES_FRESH to run the fresh PostgreSQL migration test.");
            return;
        }

        await AdoptCoreAsync(connectionString);
        await AdoptCoreAsync(connectionString);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        Assert.IsNull(await ScalarAsync<string?>(
            connection,
            "SELECT to_regclass('public.\"__EFMigrationsHistory\"')::text"));
        foreach (var table in new[]
                 {
                     "CoreUsers", "NotificationSubscriptions", "PlatformUserProfiles", "PluginOwnedRelations"
                 })
        {
            Assert.IsNotNull(await ScalarAsync<string?>(
                connection,
                $"SELECT to_regclass('public.\"{table}\"')::text"));
        }
    }

    private static async Task AdoptCoreAsync(string connectionString)
    {
        await using var core = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Core"))
            .Options);
        await EfBaselineAdopter.AdoptOrMigrateAsync(core, new EfBaselineOptions("com.ohmybot.core"));
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default! : (T)value;
    }
}
