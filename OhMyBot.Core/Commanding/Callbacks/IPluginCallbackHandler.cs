using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Commands;

namespace OhMyBot.Core.Commanding.Callbacks;

public interface IPluginCallbackHandler
{
    IReadOnlyCollection<string> ActionTypes { get; }

    Task<CommandResponse> ExecuteAsync(
        string actionType,
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        CancellationToken cancellationToken = default);
}
