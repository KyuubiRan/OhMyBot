namespace OhMyBot.Core.Commands;

[Flags]
public enum SupportedPlatforms
{
    None = 0,
    Telegram = 1,
    QQ = 2,
    All = Telegram | QQ
}
