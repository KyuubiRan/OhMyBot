using Microsoft.EntityFrameworkCore;

namespace OhMyLib;

public class OhMyDbContext : DbContext
{
    private const string DefaultPgsqlConnection =
        "Host=localhost;Port=5432;Username=postgres;Password=password;Database=oh_my_bot";

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        optionsBuilder.UseNpgsql(databaseUrl ?? DefaultPgsqlConnection)
            .UseLazyLoadingProxies();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OhMyDbContext).Assembly);
    }
}