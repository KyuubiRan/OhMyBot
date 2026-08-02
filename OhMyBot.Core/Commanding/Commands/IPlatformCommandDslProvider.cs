namespace OhMyBot.Core.Commanding.Commands;

public interface IPlatformCommandDslProvider
{
    IEnumerable<CommandDslNode> GetNodes();
}

