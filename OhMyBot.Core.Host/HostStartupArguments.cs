namespace OhMyBot.Core.Host;

internal sealed record HostStartupArguments(
    bool RemoteConsoleRequested,
    string[] HostArguments)
{
    private const string RemoteConsoleArgument = "--remote-console";

    public static HostStartupArguments Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var remoteConsoleRequested = false;
        var hostArguments = new List<string>(arguments.Count);
        foreach (var argument in arguments)
        {
            if (string.Equals(argument, RemoteConsoleArgument, StringComparison.OrdinalIgnoreCase))
            {
                remoteConsoleRequested = true;
                continue;
            }

            hostArguments.Add(argument);
        }

        return new HostStartupArguments(remoteConsoleRequested, [.. hostArguments]);
    }
}
