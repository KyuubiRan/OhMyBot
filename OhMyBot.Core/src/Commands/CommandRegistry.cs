using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Commands;

public sealed class CommandRegistry
{
    private readonly Dictionary<string, CommandRegistration> _commands = new(StringComparer.OrdinalIgnoreCase);

    public CommandRegistry(IEnumerable<CommandRegistration> registrations)
    {
        foreach (var registration in registrations)
        {
            Add(registration);
        }
    }

    public IReadOnlyCollection<CommandRegistration> Commands => _commands.Values;

    public void Add(CommandRegistration registration)
    {
        _commands[registration.Name] = registration;
    }

    public bool TryGet(string command, out CommandRegistration registration)
    {
        return _commands.TryGetValue(CommandRegistration.Normalize(command), out registration!);
    }

    public static SupportedPlatforms ToSupportedPlatform(BotPlatform platform)
    {
        return platform switch
        {
            BotPlatform.Telegram => SupportedPlatforms.Telegram,
            BotPlatform.Qq => SupportedPlatforms.QQ,
            _ => SupportedPlatforms.None
        };
    }
}
