using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Infrastructure.Identity;

namespace OhMyBot.Core.Commanding.Commands;

public sealed record CommandContext(
    CommandRequest Request,
    ResolvedIdentity Identity,
    long StartedAt,
    CancellationToken CancellationToken)
{
    public ICommandProgressReporter? Progress { get; init; }
}
