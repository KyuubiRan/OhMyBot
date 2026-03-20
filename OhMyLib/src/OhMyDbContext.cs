using Microsoft.EntityFrameworkCore;

namespace OhMyLib;

public class OhMyDbContext(DbContextOptions<OhMyDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OhMyDbContext).Assembly);
    }
}