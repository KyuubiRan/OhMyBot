using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace OhMyLib.HostedServices;

public class DatabaseAutoMigrationService(OhMyDbContext dbContext) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}