// See https://aka.ms/new-console-template for more information

using FoxTail.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        private bool _isRedisConfigured;

        public Builder()
        {
            _hostBuilder
                .UseConsoleLifetime()
                .ConfigureAppConfiguration(x =>
                {
                    x.AddJsonFile("appsettings.json", optional: true)
                     .AddEnvironmentVariables();
                });
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

        public Builder ConfigureDefaultConsoleLogging()
        {
            _hostBuilder.ConfigureServices(services =>
            {
                services.AddLogging(builder =>
                {
                    builder.AddSimpleConsole(x =>
                    {
                        x.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
                        x.UseUtcTimestamp = false;
                    });
                });
            });
            return this;
        }

        public Builder ConfigureRedisCacheIfPresent(string? prefix = null)
        {
            _hostBuilder.ConfigureServices((ctx, services) =>
            {
                var configuration = ctx.Configuration;

                var redisString = configuration.GetConnectionString("Redis");
                if (redisString.IsWhiteSpaceOrNull)
                    return;

                _isRedisConfigured = true;
                services.AddStackExchangeRedisCache(x =>
                {
                    x.Configuration = redisString;
                    x.InstanceName = prefix ?? "OhMyBot:";
                });
            });

            return this;
        }

        public Builder ConfigureDefaultDatabase()
        {
            _hostBuilder.ConfigureServices((ctx, services) =>
            {
                var configuration = ctx.Configuration;

                var dbString = configuration.GetConnectionString("Database");
                if (!dbString.IsWhiteSpaceOrNull)
                {
                    Environment.SetEnvironmentVariable("DATABASE_URL", dbString);
                }

                services.AddDbContext<OhMyDbContext>()
                        .AddHostedService<DatabaseAutoMigrationService>();
            });


            return this;
        }

        public MyBotApplication Build()
        {
            if (!_isRedisConfigured)
            {
                _hostBuilder.ConfigureServices(services => { services.AddMemoryCache(); });
            }

            var app = new MyBotApplication(_hostBuilder.Build());

            return app;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _host.RunAsync(cancellationToken);
    }
}