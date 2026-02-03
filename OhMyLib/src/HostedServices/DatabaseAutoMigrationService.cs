using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace OhMyLib.HostedServices;

public class DatabaseAutoMigrationService(OhMyDbContext context) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await context.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}