using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Commanding.Callbacks;
using OhMyBot.Core.Commanding.Commands;

namespace OhMyBot.Plugin.Commanding;

public delegate Task<CommandResponse> PluginCommandHandlerDelegate(CommandContext context);

public delegate Task<CommandResponse> PluginCallbackHandlerDelegate(
    CommandContext context,
    CallbackAction action,
    string editMessageId,
    CancellationToken cancellationToken);

public interface IPluginCommandMiddleware
{
    Task<CommandResponse> InvokeAsync(
        CommandContext context,
        PluginCommandHandlerDelegate next);
}

public interface IPluginCallbackMiddleware
{
    Task<CommandResponse> InvokeAsync(
        CommandContext context,
        CallbackAction action,
        string editMessageId,
        PluginCallbackHandlerDelegate next,
        CancellationToken cancellationToken = default);
}
