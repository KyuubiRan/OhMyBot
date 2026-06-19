using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Identity;

namespace OhMyBot.Core.Commands;

public sealed record CommandContext(
    CommandRequest Request,
    ResolvedIdentity Identity,
    long StartedAt,
    CancellationToken CancellationToken);
