using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OhMyBot.Plugin.Abstractions;

namespace OhMyBot.Plugin.Commanding;

public interface ICommandPluginBuilder
{
    IPluginBuilder Plugin { get; }

    IServiceCollection Services { get; }

    IConfiguration Configuration { get; }
}

internal sealed class CommandPluginBuilder(IPluginBuilder plugin) : ICommandPluginBuilder
{
    public IPluginBuilder Plugin { get; } = plugin;

    public IServiceCollection Services => Plugin.Services;

    public IConfiguration Configuration => Plugin.Configuration;
}
