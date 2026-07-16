using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using OhMyBot.Plugin.Abstractions;

namespace OhMyBot.Core.Infrastructure.Plugins;

public sealed record EfBaselineOptions(
    string PluginId,
    string HistoryTable,
    IReadOnlyList<string> OwnedTables,
    string? RelationTable = null,
    string RelationCoreUserIdColumn = "CoreUserId");

/// <summary>
/// Adopts the EF baseline on an existing bridged v2 database, or creates the
/// schema normally on a fresh database. Existing schemas are stamped only
/// after a strict catalog comparison.
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
    public const string LegacyBridgeMigration = "20260712133137_BridgePluginOwnedRelations";
    private const string EfProductVersion = "10.0.9";

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

        var migrations = dbContext.Database.GetMigrations().ToArray();
        if (migrations.Length == 0)
        {
            throw new InvalidOperationException($"{options.PluginId} 没有可用的 EF migration。");
        }

        var baselineMigration = migrations[0];
        var history = dbContext.GetService<IHistoryRepository>();
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(hashtext('ohmybot:ef-baseline-adoption'))",
                cancellationToken);

            var historyExists = await ScalarAsync<bool>(dbContext,
                "SELECT to_regclass(@name) IS NOT NULL",
                [("name", $"public.\"{options.HistoryTable}\"")],
                cancellationToken);
            var applied = historyExists
                ? await history.GetAppliedMigrationsAsync(cancellationToken)
                : [];
            if (applied.Count == 0)
            {
                var existingOwnedTables = await CountExistingTablesAsync(
                    dbContext, options.OwnedTables, cancellationToken);
                if (existingOwnedTables > 0)
                {
                    if (!await LegacyBridgeAppliedAsync(dbContext, cancellationToken))
                    {
                        throw new InvalidOperationException(
                            $"{options.PluginId} 检测到已有业务表，但 legacy migration history 未到 {LegacyBridgeMigration}，拒绝自动 adoption。");
                    }

                    await ValidateSchemaAsync(dbContext, options.OwnedTables, cancellationToken);
                    await dbContext.Database.ExecuteSqlRawAsync(
                        history.GetCreateIfNotExistsScript(), cancellationToken);
                    await dbContext.Database.ExecuteSqlRawAsync(
                        history.GetInsertScript(new HistoryRow(baselineMigration, EfProductVersion)),
                        cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }

        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private static async Task<int> CountExistingTablesAsync(
        DbContext dbContext,
        IReadOnlyList<string> tables,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var table in tables)
        {
            if (await ScalarAsync<bool>(dbContext,
                    "SELECT to_regclass(@name) IS NOT NULL",
                    [("name", $"public.\"{table}\"")],
                    cancellationToken))
            {
                count++;
            }
        }

        return count;
    }

    private static async Task<bool> LegacyBridgeAppliedAsync(
        DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var historyExists = await ScalarAsync<bool>(dbContext,
            "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL",
            [], cancellationToken);
        if (!historyExists)
        {
            return false;
        }

        return await ScalarAsync<bool>(dbContext,
            "SELECT EXISTS (SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @migration)",
            [("migration", LegacyBridgeMigration)], cancellationToken);
    }

    private static async Task ValidateSchemaAsync(
        DbContext dbContext,
        IReadOnlyList<string> ownedTables,
        CancellationToken cancellationToken)
    {
        var relationalModel = dbContext.Model.GetRelationalModel();
        foreach (var tableName in ownedTables)
        {
            var expected = relationalModel.Tables.SingleOrDefault(table =>
                table.Name == tableName && (table.Schema is null or "public"))
                ?? throw new InvalidOperationException($"EF model 未包含 public.{tableName}。");

            var actualColumns = await ReadColumnsAsync(dbContext, tableName, cancellationToken);
            var expectedColumns = expected.Columns.ToDictionary(column => column.Name, StringComparer.Ordinal);
            EnsureSameNames($"public.{tableName} columns", expectedColumns.Keys, actualColumns.Keys);
            foreach (var (columnName, column) in expectedColumns)
            {
                var actual = actualColumns[columnName];
                if (!string.Equals(NormalizeType(column.StoreType), NormalizeType(actual.StoreType), StringComparison.Ordinal)
                    || column.IsNullable != actual.IsNullable
                    || IsIdentity(column) != actual.IsIdentity
                    || !DefaultMatches(column, actual.DefaultExpression))
                {
                    throw new InvalidOperationException(
                        $"Schema drift: public.{tableName}.{columnName} 与 EF baseline 不一致。" +
                        $" expected={column.StoreType}, nullable={column.IsNullable}, identity={IsIdentity(column)}, default={column.DefaultValue ?? column.DefaultValueSql};" +
                        $" actual={actual.StoreType}, nullable={actual.IsNullable}, identity={actual.IsIdentity}, default={actual.DefaultExpression}");
                }
            }

            var actualIndexes = await ReadIndexesAsync(dbContext, tableName, cancellationToken);
            var expectedIndexes = expected.Indexes.ToDictionary(index => index.Name, StringComparer.Ordinal);
            EnsureSameNames($"public.{tableName} indexes", expectedIndexes.Keys, actualIndexes.Keys);
            foreach (var (name, index) in expectedIndexes)
            {
                var actual = actualIndexes[name];
                if (index.IsUnique != actual.IsUnique
                    || !index.Columns.Select(column => column.Name).SequenceEqual(actual.Columns, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException($"Schema drift: index {name} 与 EF baseline 不一致。");
                }
            }

            var actualForeignKeys = await ReadForeignKeysAsync(dbContext, tableName, cancellationToken);
            var expectedForeignKeys = expected.ForeignKeyConstraints.ToDictionary(foreignKey => foreignKey.Name, StringComparer.Ordinal);
            EnsureSameNames($"public.{tableName} foreign keys", expectedForeignKeys.Keys, actualForeignKeys.Keys);
            foreach (var (name, foreignKey) in expectedForeignKeys)
            {
                var actual = actualForeignKeys[name];
                if (!foreignKey.Columns.Select(column => column.Name).SequenceEqual(actual.Columns, StringComparer.Ordinal)
                    || !foreignKey.PrincipalColumns.Select(column => column.Name).SequenceEqual(actual.PrincipalColumns, StringComparer.Ordinal)
                    || !string.Equals(foreignKey.PrincipalTable.Name, actual.PrincipalTable, StringComparison.Ordinal)
                    || foreignKey.OnDeleteAction != actual.OnDeleteAction)
                {
                    throw new InvalidOperationException($"Schema drift: foreign key {name} 与 EF baseline 不一致。");
                }
            }

            var actualPrimaryKey = await ReadPrimaryKeyAsync(dbContext, tableName, cancellationToken);
            var expectedPrimaryKey = expected.PrimaryKey
                ?? throw new InvalidOperationException($"EF model 的 {tableName} 没有主键。");
            if (!string.Equals(expectedPrimaryKey.Name, actualPrimaryKey.Name, StringComparison.Ordinal)
                || !expectedPrimaryKey.Columns.Select(column => column.Name)
                    .SequenceEqual(actualPrimaryKey.Columns, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"Schema drift: public.{tableName} primary key 与 EF baseline 不一致。");
            }
        }
    }

    private static bool IsIdentity(IColumn column)
        => column.PropertyMappings.Any(mapping =>
            mapping.Property.FindAnnotation("Npgsql:ValueGenerationStrategy")?.Value?.ToString()
                ?.Contains("Identity", StringComparison.Ordinal) == true);

    private static bool DefaultMatches(IColumn expected, string? actual)
    {
        if (IsIdentity(expected))
        {
            return true;
        }

        var expectedDefault = expected.DefaultValueSql ?? expected.DefaultValue?.ToString();
        if (expectedDefault is null)
        {
            return actual is null;
        }

        return NormalizeDefault(expectedDefault) == NormalizeDefault(actual);
    }

    private static string NormalizeDefault(string? value)
        => (value ?? string.Empty).Replace("::text", string.Empty, StringComparison.Ordinal)
            .Trim('(', ')', '\'', ' ', '"');

    private static string NormalizeType(string value)
        => value.Replace("varchar", "character varying", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();

    private static void EnsureSameNames(string subject, IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
        var actualSet = actual.ToHashSet(StringComparer.Ordinal);
        if (!expectedSet.SetEquals(actualSet))
        {
            throw new InvalidOperationException(
                $"Schema drift: {subject} 不一致。expected=[{string.Join(',', expectedSet.Order())}], actual=[{string.Join(',', actualSet.Order())}]");
        }
    }

    private static async Task<Dictionary<string, ActualColumn>> ReadColumnsAsync(
        DbContext dbContext, string table, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT a.attname, pg_catalog.format_type(a.atttypid, a.atttypmod),
                   NOT a.attnotnull, a.attidentity <> '', pg_get_expr(d.adbin, d.adrelid)
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
            WHERE n.nspname = 'public' AND c.relname = @table
              AND a.attnum > 0 AND NOT a.attisdropped
            ORDER BY a.attnum
            """;
        var result = new Dictionary<string, ActualColumn>(StringComparer.Ordinal);
        await ReadAsync(dbContext, sql, [("table", table)], cancellationToken, reader =>
        {
            result.Add(reader.GetString(0), new ActualColumn(
                reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        });
        return result;
    }

    private static async Task<Dictionary<string, ActualIndex>> ReadIndexesAsync(
        DbContext dbContext, string table, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT i.relname, ix.indisunique,
                   array_agg(a.attname ORDER BY ord.ordinality)
            FROM pg_index ix
            JOIN pg_class t ON t.oid = ix.indrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            JOIN pg_class i ON i.oid = ix.indexrelid
            JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY ord(attnum, ordinality) ON true
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ord.attnum
            WHERE n.nspname = 'public' AND t.relname = @table AND NOT ix.indisprimary
            GROUP BY i.relname, ix.indisunique
            """;
        var result = new Dictionary<string, ActualIndex>(StringComparer.Ordinal);
        await ReadAsync(dbContext, sql, [("table", table)], cancellationToken, reader =>
            result.Add(reader.GetString(0), new ActualIndex(reader.GetBoolean(1), reader.GetFieldValue<string[]>(2))));
        return result;
    }

    private static async Task<Dictionary<string, ActualForeignKey>> ReadForeignKeysAsync(
        DbContext dbContext, string table, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT con.conname, pt.relname,
                   array_agg(sa.attname ORDER BY src.ordinality),
                   array_agg(pa.attname ORDER BY src.ordinality), con.confdeltype
            FROM pg_constraint con
            JOIN pg_class st ON st.oid = con.conrelid
            JOIN pg_namespace sn ON sn.oid = st.relnamespace
            JOIN pg_class pt ON pt.oid = con.confrelid
            JOIN LATERAL unnest(con.conkey) WITH ORDINALITY src(attnum, ordinality) ON true
            JOIN LATERAL unnest(con.confkey) WITH ORDINALITY dst(attnum, ordinality) ON dst.ordinality = src.ordinality
            JOIN pg_attribute sa ON sa.attrelid = st.oid AND sa.attnum = src.attnum
            JOIN pg_attribute pa ON pa.attrelid = pt.oid AND pa.attnum = dst.attnum
            WHERE con.contype = 'f' AND sn.nspname = 'public' AND st.relname = @table
            GROUP BY con.conname, pt.relname, con.confdeltype
            """;
        var result = new Dictionary<string, ActualForeignKey>(StringComparer.Ordinal);
        await ReadAsync(dbContext, sql, [("table", table)], cancellationToken, reader =>
            result.Add(reader.GetString(0), new ActualForeignKey(
                reader.GetString(1), reader.GetFieldValue<string[]>(2), reader.GetFieldValue<string[]>(3),
                reader.GetChar(4) switch
                {
                    'c' => ReferentialAction.Cascade,
                    'n' => ReferentialAction.SetNull,
                    'd' => ReferentialAction.SetDefault,
                    'r' => ReferentialAction.Restrict,
                    _ => ReferentialAction.NoAction
                })));
        return result;
    }

    private static async Task<ActualPrimaryKey> ReadPrimaryKeyAsync(
        DbContext dbContext, string table, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT con.conname, array_agg(a.attname ORDER BY ord.ordinality)
            FROM pg_constraint con
            JOIN pg_class t ON t.oid = con.conrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            JOIN LATERAL unnest(con.conkey) WITH ORDINALITY ord(attnum, ordinality) ON true
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ord.attnum
            WHERE con.contype = 'p' AND n.nspname = 'public' AND t.relname = @table
            GROUP BY con.conname
            """;
        ActualPrimaryKey? result = null;
        await ReadAsync(dbContext, sql, [("table", table)], cancellationToken, reader =>
            result = new ActualPrimaryKey(reader.GetString(0), reader.GetFieldValue<string[]>(1)));
        return result ?? throw new InvalidOperationException($"Schema drift: public.{table} 缺少主键。");
    }

    private static async Task<T> ScalarAsync<T>(DbContext dbContext, string sql,
        IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        AddParameters(command, parameters);
        return (T)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Database scalar query returned null."));
    }

    private static async Task ReadAsync(DbContext dbContext, string sql,
        IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellationToken,
        Action<DbDataReader> read)
    {
        var connection = dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        AddParameters(command, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            read(reader);
        }
    }

    private static async Task EnsureOpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
    }

    private static void AddParameters(DbCommand command, IReadOnlyList<(string Name, object Value)> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }

    private sealed record ActualColumn(string StoreType, bool IsNullable, bool IsIdentity, string? DefaultExpression);
    private sealed record ActualIndex(bool IsUnique, string[] Columns);
    private sealed record ActualForeignKey(string PrincipalTable, string[] Columns, string[] PrincipalColumns, ReferentialAction OnDeleteAction);
    private sealed record ActualPrimaryKey(string Name, string[] Columns);
}
