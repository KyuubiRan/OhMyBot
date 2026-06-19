using OhMyBot.Contracts.Grpc;

namespace OhMyBot.Core.Commands;

public sealed class PingCommand(TimeProvider timeProvider) : ICoreCommand
{
    public string Name => "ping";

    public string Description => "Check Core connectivity.";

    public string Usage => "/ping";

    public IReadOnlyList<string> Aliases => [];

    public UserPrivilege RequiredPrivilege => UserPrivilege.User;

    public SupportedPlatforms SupportPlatforms => SupportedPlatforms.All;

    public bool Enabled => true;

    public Task<CommandResponse> ExecuteAsync(CommandContext context)
    {
        var elapsed = timeProvider.GetElapsedTime(context.StartedAt);
        var elapsedMs = elapsed.Ticks / TimeSpan.TicksPerMillisecond;
        var response = CommandResponses.Ok(CommandResponseDataKind.Ping, context);
        response.Ping = new PingData { ElapsedMs = elapsedMs };
        return Task.FromResult(response);
    }
}
