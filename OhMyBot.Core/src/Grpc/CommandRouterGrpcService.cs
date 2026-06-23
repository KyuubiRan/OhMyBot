using Grpc.Core;
using OhMyBot.Contracts.Grpc;
using OhMyBot.Core.Admin;
using OhMyBot.Core.Callbacks;
using OhMyBot.Core.Commands;
using OhMyBot.Core.Terminal;
using OhMyBot.Core.UserProfiles;

namespace OhMyBot.Core.Grpc;

public sealed class CommandRouterGrpcService(
    CommandExecutionService commandExecutionService,
    CallbackExecutionService callbackExecutionService,
    PlatformUserProfileService userProfileService,
    IServiceScopeFactory scopeFactory,
    InteractiveConsoleOutputQueue consoleOutputQueue,
    ILogger<CommandRouterGrpcService> logger) : CommandRouter.CommandRouterBase
{
    public override Task<CommandResponse> ExecuteCommand(CommandRequest request, ServerCallContext context)
    {
        return commandExecutionService.ExecuteAsync(request, context.CancellationToken);
    }

    public override Task<GetRoutesResponse> GetRoutes(GetRoutesRequest request, ServerCallContext context)
    {
        return commandExecutionService.GetRoutesAsync(request, context.CancellationToken);
    }

    public override Task<CommandResponse> ExecuteCallback(CallbackRequest request, ServerCallContext context)
    {
        return callbackExecutionService.ExecuteAsync(request, context.CancellationToken);
    }

    public override async Task<UserProfileResponse> RecordUserProfile(UserProfileRequest request, ServerCallContext context)
    {
        await userProfileService.RecordAsync(request, context.CancellationToken);
        return new UserProfileResponse { Recorded = true };
    }

    public override async Task OpenAdminConsole(
        IAsyncStreamReader<AdminConsoleInput> requestStream,
        IServerStreamWriter<AdminConsoleOutput> responseStream,
        ServerCallContext context)
    {
        using var stopLogs = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var writeGate = new SemaphoreSlim(1, 1);

        async Task WriteAsync(AdminConsoleOutput output, CancellationToken cancellationToken)
        {
            await writeGate.WaitAsync(cancellationToken);
            try
            {
                await responseStream.WriteAsync(output, cancellationToken);
            }
            finally
            {
                writeGate.Release();
            }
        }

        var logTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in consoleOutputQueue.ReadAllAsync(stopLogs.Token))
                {
                    await WriteAsync(ToOutput(item.Segments), stopLogs.Token);
                }
            }
            catch (OperationCanceledException) when (stopLogs.IsCancellationRequested)
            {
            }
        }, CancellationToken.None);

        await WriteAsync(ToOutput([new ConsoleTextSegment("Connected to OhMyBot Core. Type 'exit' to leave remote console.")]), context.CancellationToken);

        try
        {
            while (await requestStream.MoveNext(context.CancellationToken))
            {
                var commandLine = requestStream.Current.CommandLine.Trim();
                if (string.IsNullOrWhiteSpace(commandLine))
                {
                    continue;
                }

                if (IsExitCommand(commandLine))
                {
                    await WriteAsync(ToOutput([new ConsoleTextSegment("Leaving remote console.")]), context.CancellationToken);
                    break;
                }

                await ExecuteAdminCommandAsync(commandLine, WriteAsync, context.CancellationToken);
            }
        }
        finally
        {
            await stopLogs.CancelAsync();
            await logTask;
        }
    }

    private async Task ExecuteAdminCommandAsync(
        string commandLine,
        Func<AdminConsoleOutput, CancellationToken, Task> writeAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var executor = scope.ServiceProvider.GetRequiredService<AdminCommandExecutor>();
            var result = await executor.ExecuteAsync(commandLine, cancellationToken);
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                var message = result.Success ? result.Message : $"Error: {result.Message}";
                await writeAsync(ToOutput([new ConsoleTextSegment(message)]), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to execute remote admin console command.");
            await writeAsync(ToOutput([new ConsoleTextSegment($"Error: {exception.Message}", ConsoleColor.Red)]), cancellationToken);
        }
    }

    private static AdminConsoleOutput ToOutput(IReadOnlyList<ConsoleTextSegment> segments)
    {
        var output = new AdminConsoleOutput();
        foreach (var segment in segments)
        {
            var grpcSegment = new AdminConsoleTextSegment { Text = segment.Text };
            if (segment.ForegroundColor is not null)
            {
                grpcSegment.ForegroundColor = (int)segment.ForegroundColor.Value;
            }

            if (segment.BackgroundColor is not null)
            {
                grpcSegment.BackgroundColor = (int)segment.BackgroundColor.Value;
            }

            output.Segments.Add(grpcSegment);
        }

        return output;
    }

    private static bool IsExitCommand(string commandLine)
    {
        return string.Equals(commandLine, "exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(commandLine, "quit", StringComparison.OrdinalIgnoreCase);
    }
}
