// See https://aka.ms/new-console-template for more information

using FoxTail.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OhMyLib.HostedServices;

namespace OhMyLib;

public sealed class MyBotApplication
{
    private readonly IHost _host;
    public IServiceProvider Services => _host.Services;

    private MyBotApplication(IHost host)
    {
        _host = host;
    }

    public class Builder
    {
        private readonly HostBuilder _hostBuilder = new();
        private bool _isBuild;

        public Builder()
        {
            _hostBuilder
                .UseConsoleLifetime()
                .ConfigureAppConfiguration(x =>
                {
                    x.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                     .AddEnvironmentVariables();
                })
                .ConfigureLogging((context, builder) => builder.AddConfiguration(context.Configuration.GetSection("Logging")));
        }

        public Builder ConfigureServices(Action<HostBuilderContext, IServiceCollection> configure)
        {
            _hostBuilder.ConfigureServices(configure);
            return this;
        }

        public Builder ConfigureServices(Action<IServiceCollection> configure)
        {
            _hostBuilder.ConfigureServices(configure);
            return this;
        }

        public Builder ConfigureRedisIfPresent(string? prefix = null)
        {
            ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;

                var redisString = configuration.GetConnectionString("Redis");
                if (!redisString.IsWhiteSpaceOrNull)
                {
                    services.RemoveAll<IDistributedCache>();
                    services.AddStackExchangeRedisCache(x =>
                    {
                        x.Configuration = redisString;
                        x.InstanceName = prefix ?? "OhMyBot:";
                    });
                }
            });

            return this;
        }

        public Builder ConfigureDefaultConsoleLogging()
        {
            _hostBuilder.ConfigureLogging(builder =>
            {
                builder.AddSimpleConsole(x =>
                {
                    x.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
                    x.UseUtcTimestamp = false;
                });
            });
            return this;
        }

        public Builder ConfigureDefaultDatabase()
        {
            _hostBuilder.ConfigureServices((ctx, services) =>
            {
                var configuration = ctx.Configuration;

                const string defaultPgsqlConnectionString = "Host=localhost;Port=5432;Username=postgres;Password=password;Database=oh_my_bot";

                var dbString = configuration
                               .GetConnectionString("Database")
                               .IfWhiteSpaceOrNull(Environment.GetEnvironmentVariable("DATABASE_URL"))
                               .IfWhiteSpaceOrNull(defaultPgsqlConnectionString);

                services
                    .AddDbContextFactory<OhMyDbContext>(options =>
                    {
                        options.UseNpgsql(dbString)
                               .UseLazyLoadingProxies();
                    })
                    .AddHostedService<DatabaseAutoMigrationService>();
            });

            return this;
        }

        public MyBotApplication Build()
        {
            if (_isBuild)
                throw new InvalidOperationException("Build() can only be called once");
            _isBuild = true;

            _hostBuilder.ConfigureServices(services => services.AddDistributedMemoryCache());

            var app = new MyBotApplication(_hostBuilder.Build());

            return app;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _host.RunAsync(cancellationToken);
    }
}