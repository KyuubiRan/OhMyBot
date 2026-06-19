using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commands;

namespace OhMyBot.Core.Routing;

public sealed record RouteEntry(
    string Command,
    string CoreCommand,
    string Description,
    string Usage,
    UserPrivilege RequiredPrivilege,
    SupportedPlatforms SupportPlatforms,
    bool Enabled,
    bool TargetExists,
    UserPrivilege EffectiveRequiredPrivilege)
{
    public RouteDescriptor ToDescriptor() => new()
    {
        Command = Command,
        CoreCommand = CoreCommand,
        Description = Description,
        Usage = Usage,
        RequiredPrivilege = RequiredPrivilege,
        SupportPlatforms = (int)SupportPlatforms,
        Enabled = Enabled
    };
}
