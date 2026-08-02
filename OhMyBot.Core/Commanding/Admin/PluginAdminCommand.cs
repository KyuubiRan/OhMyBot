using OhMyBot.Core.Infrastructure.Plugins;

namespace OhMyBot.Core.Commanding.Admin;

public sealed class PluginAdminCommand(IPluginManager pluginManager) : IAdminCommand
{
    private static readonly AdminCommandDefinition CommandDefinition = new(
        "plugin",
        "plugin <list|status|reload|enable|disable> [id|all]",
        "Manage runtime plugins.",
        [],
        [],
        [
            "plugin list",
            "plugin status com.ohmybot.kuro",
            "plugin reload com.ohmybot.kuro",
            "plugin reload all",
            "plugin disable com.example.plugin",
            "plugin enable com.example.plugin"
        ]);

    public AdminCommandDefinition Definition => CommandDefinition;

    public async Task<AdminCommandResult> ExecuteAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count == 0)
        {
            return AdminCommandResult.Error("Usage: " + Definition.Usage);
        }

        switch (args[0].Trim().ToLowerInvariant())
        {
            case "list":
                return AdminCommandResult.Ok(FormatList(pluginManager.GetPlugins()));
            case "status" when args.Count >= 2:
                var plugin = pluginManager.GetPlugins().FirstOrDefault(item =>
                    string.Equals(item.Id, args[1], StringComparison.OrdinalIgnoreCase));
                return plugin is null
                    ? AdminCommandResult.Error($"Unknown plugin: {args[1]}.")
                    : AdminCommandResult.Ok(Format(plugin));
            case "reload" when args.Count >= 2:
                var result = await pluginManager.ReloadAsync(args[1], cancellationToken);
                return result.Success
                    ? AdminCommandResult.Ok(result.Message)
                    : AdminCommandResult.Error(result.Message);
            case "disable" when args.Count >= 2:
                var disable = await pluginManager.DisableAsync(args[1], cancellationToken);
                return disable.Success
                    ? AdminCommandResult.Ok(disable.Message)
                    : AdminCommandResult.Error(disable.Message);
            case "enable" when args.Count >= 2:
                var enable = await pluginManager.EnableAsync(args[1], cancellationToken);
                return enable.Success
                    ? AdminCommandResult.Ok(enable.Message)
                    : AdminCommandResult.Error(enable.Message);
            default:
                return AdminCommandResult.Error("Usage: " + Definition.Usage);
        }
    }

    private static string FormatList(IReadOnlyList<PluginRuntimeInfo> plugins)
    {
        return plugins.Count == 0
            ? "No plugins discovered."
            : string.Join(Environment.NewLine, plugins.Select(plugin =>
                $"{plugin.Id} {plugin.Version} [{plugin.State}] platforms={plugin.SupportedPlatforms} generation={plugin.Generation}"));
    }

    private static string Format(PluginRuntimeInfo plugin)
    {
        return string.Join(Environment.NewLine,
            $"Id: {plugin.Id}",
            $"Name: {plugin.Name}",
            $"Version: {plugin.Version}",
            $"State: {plugin.State}",
            $"Priority: {plugin.LoadPriority}",
            $"Platforms: {plugin.SupportedPlatforms}",
            $"Generation: {plugin.Generation}",
            $"Dependencies: {(plugin.Dependencies.Count == 0 ? "-" : string.Join(", ", plugin.Dependencies.Select(item => item.PluginId)))}",
            $"LastError: {plugin.LastError ?? "-"}");
    }
}
