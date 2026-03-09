using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OhMyLib.HostedServices;

public class DatabaseAutoMigrationService(IServiceScopeFactory serviceFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scoped = serviceFactory.CreateAsyncScope();
        await scoped.ServiceProvider.GetRequiredService<OhMyDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}