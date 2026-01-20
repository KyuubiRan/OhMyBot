// See https://aka.ms/new-console-template for more information

using FoxTail.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLib.HostedServices;

namespace OhMyLib;

public sealed class MyBotApplication : IDisposable
{
    private readonly ServiceProvider _sp;

    public IServiceProvider ServiceProvider => _sp;
    public readonly ConfigurationManager Configuration;

    private MyBotApplication(ServiceProvider serviceProvider, ConfigurationManager configuration)
    {
        _sp = serviceProvider;
        Configuration = configuration;
    }

    public class Builder
    {
        public readonly IServiceCollection Services = new ServiceCollection();
        public readonly ConfigurationManager Configuration = new();

        public Builder ConfigureServices(Action<IServiceCollection> configure)
        {
            configure(Services);
            return this;
        }

        public Builder ConfigureServices(Action<IServiceCollection, ConfigurationManager> configure)
        {
            configure(Services, Configuration);
            return this;
        }

        public Builder ConfigDefaultConsoleLogging()
        {
            Services.AddLogging(builder =>
            {
                builder.AddSimpleConsole(x =>
                {
                    x.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
                    x.UseUtcTimestamp = false;
                });
            });
            return this;
        }

        public Builder ConfigDefaultDatabase()
        {
            var dbString = Configuration.GetConnectionString("Database");
            if (!dbString.IsWhiteSpaceOrNull)
            {
                Environment.SetEnvironmentVariable("DATABASE_URL", dbString);
            }

            Services.AddDbContext<OhMyDbContext>()
                    .AddHostedService<DatabaseAutoMigrationService>();

            return this;
        }

        public Builder ConfigDefaultConfiguration()
        {
            Configuration.AddJsonFile("appsettings.json", optional: true)
                         .AddEnvironmentVariables();
            return this;
        }

        public MyBotApplication Build()
        {
            var app = new MyBotApplication(
                serviceProvider: Services.BuildServiceProvider(),
                configuration: Configuration
            );

            return app;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var hostedServices = _sp.GetServices<IHostedService>();
        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(cancellationToken);
        }

        try
        {
            await Task.Delay(-1, cancellationToken);
        }
        catch (TaskCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _sp.Dispose();
    }
}