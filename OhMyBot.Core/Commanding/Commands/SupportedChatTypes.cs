namespace OhMyBot.Core.Commanding.Commands;

[Flags]
public enum SupportedChatTypes
{
    None = 0,
    Private = 1,
    Group = 2,
    All = Private | Group
}
