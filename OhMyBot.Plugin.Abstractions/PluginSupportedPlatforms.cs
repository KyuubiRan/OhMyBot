namespace OhMyBot.Plugin.Abstractions;

[Flags]
public enum PluginSupportedPlatforms
{
    None = 0,
    Telegram = 1,
    QQ = 2,
    All = Telegram | QQ
}
