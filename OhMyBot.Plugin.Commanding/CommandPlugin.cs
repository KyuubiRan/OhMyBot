using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OhMyBot.Plugin.Abstractions;

namespace OhMyBot.Plugin.Commanding;

/// <summary>
/// Convenience base class for the common case of a plugin contributing commands and related components.
/// </summary>
public abstract class CommandPlugin : BasicPlugin
{
    public sealed override void Configure(IPluginBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ConfigureServices(builder.Services, builder.Configuration);
        ConfigureCommanding(new CommandPluginBuilder(builder));
    }

    /// <summary>
    /// 注册插件自身的服务与配置。<paramref name="configuration"/> 是本插件的 pluginsettings.json
    /// 配置根，与 <see cref="ICommandPluginBuilder.Configuration"/> 以及插件容器中注册的
    /// <see cref="IConfiguration"/> 是同一实例（见 PluginManager 建 PluginBuilder 处），三者可互换。
    /// </summary>
    protected virtual void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
    }

    protected abstract void ConfigureCommanding(ICommandPluginBuilder builder);
}
