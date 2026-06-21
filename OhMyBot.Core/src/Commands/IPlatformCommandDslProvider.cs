namespace OhMyBot.Core.Commands;

public interface IPlatformCommandDslProvider
{
    IEnumerable<CommandDslNode> GetNodes();
}

