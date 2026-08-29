using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Commands;

namespace OhMyBot.Core.Commanding.Routing;

public sealed record RouteEntry(
    string Command,
    string CoreCommand,
    string Description,
    string Usage,
    IReadOnlyList<string> Aliases,
    UserPrivilege RequiredPrivilege,
    SupportedPlatforms SupportPlatforms,
    SupportedChatTypes SupportChatTypes,
    bool AcceptsReplyMedia,
    CommandProgressStyle ProgressStyle,
    bool Enabled,
    bool TargetExists,
    UserPrivilege EffectiveRequiredPrivilege,
    SupportedChatTypes EffectiveSupportChatTypes)
{
    public RouteDescriptor ToDescriptor() => new()
    {
        Command = Command,
        CoreCommand = CoreCommand,
        Description = Description,
        Usage = Usage,
        Aliases = { Aliases },
        RequiredPrivilege = RequiredPrivilege,
        SupportPlatforms = (int)SupportPlatforms,
        SupportChatTypes = (int)SupportChatTypes,
        AcceptsReplyMedia = AcceptsReplyMedia,
        ProgressStyle = ProgressStyle,
        Enabled = Enabled
    };
}
