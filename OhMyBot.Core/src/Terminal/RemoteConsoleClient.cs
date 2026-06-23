using Grpc.Core;
using Grpc.Net.Client;
using OhMyBot.Contracts.Grpc;
using System.Threading.Channels;

namespace OhMyBot.Core.Terminal;

public static class RemoteConsoleClient
{
    public static async Task<bool> TryRunAsync(IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return false;
        }

        var coreAddress = configuration["Core:GrpcAddress"] ?? "http://localhost:5100";
        var handler = new SocketsHttpHandler { UseProxy = false };
        using var channel = GrpcChannel.ForAddress(coreAddress, new GrpcChannelOptions { HttpHandler = handler });
        var client = new CommandRouter.CommandRouterClient(channel);

        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCts.CancelAfter(TimeSpan.FromMilliseconds(800));

        AsyncDuplexStreamingCall<AdminConsoleInput, AdminConsoleOutput>? call = null;
        try
        {
            call = client.OpenAdminConsole(cancellationToken: cancellationToken);
            if (!await call.ResponseStream.MoveNext(probeCts.Token))
            {
                call.Dispose();
                return false;
            }
        }
        catch (RpcException)
        {
            call?.Dispose();
            return false;
        }
        catch (OperationCanceledException)
        {
            call?.Dispose();
            return false;
        }

        using (call)
        {
            using var callCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var consoleState = new InteractiveConsoleState();
            var outputs = Channel.CreateUnbounded<AdminConsoleOutput>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });

            outputs.Writer.TryWrite(call.ResponseStream.Current);
            var readTask = ReadOutputsAsync(call.ResponseStream, outputs.Writer, callCts.Token);
            await RunInputLoopAsync(consoleState, call.RequestStream, outputs.Reader, readTask, callCts.Token);
            await call.RequestStream.CompleteAsync();
            await callCts.CancelAsync();
            await readTask;
            DrainOutputs(consoleState, outputs.Reader);
        }

        return true;
    }

    private static async Task RunInputLoopAsync(
        InteractiveConsoleState consoleState,
        IClientStreamWriter<AdminConsoleInput> requestStream,
        ChannelReader<AdminConsoleOutput> outputs,
        Task readTask,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            DrainOutputs(consoleState, outputs);
            if (readTask.IsCompleted)
            {
                return;
            }

            if (!Console.KeyAvailable)
            {
                await Task.Delay(50, cancellationToken);
                continue;
            }

            var key = Console.ReadKey(intercept: true);

            if (key.Key is ConsoleKey.Enter)
            {
                var commandLine = consoleState.CommitInput().Trim();
                if (string.IsNullOrWhiteSpace(commandLine))
                {
                    consoleState.RenderPrompt();
                    continue;
                }

                await requestStream.WriteAsync(new AdminConsoleInput { CommandLine = commandLine }, cancellationToken);
                if (IsExitCommand(commandLine))
                {
                    return;
                }

                consoleState.RenderPrompt();
                continue;
            }

            if (key.Key is ConsoleKey.Backspace)
            {
                consoleState.Backspace();
                continue;
            }

            if (key.Key is ConsoleKey.UpArrow)
            {
                consoleState.PreviousHistory();
                continue;
            }

            if (key.Key is ConsoleKey.DownArrow)
            {
                consoleState.NextHistory();
                continue;
            }

            if (key.Key is ConsoleKey.LeftArrow)
            {
                consoleState.MoveCursorLeft();
                continue;
            }

            if (key.Key is ConsoleKey.RightArrow)
            {
                consoleState.MoveCursorRight();
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                consoleState.Append(key.KeyChar);
            }
        }
    }

    private static async Task ReadOutputsAsync(
        IAsyncStreamReader<AdminConsoleOutput> responseStream,
        ChannelWriter<AdminConsoleOutput> outputs,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await responseStream.MoveNext(cancellationToken))
            {
                await outputs.WriteAsync(responseStream.Current, cancellationToken);
            }
        }
        catch (RpcException exception) when (exception.StatusCode is StatusCode.Cancelled or StatusCode.Unavailable)
        {
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            outputs.TryComplete();
        }
    }

    private static void DrainOutputs(InteractiveConsoleState consoleState, ChannelReader<AdminConsoleOutput> outputs)
    {
        while (outputs.TryRead(out var output))
        {
            WriteOutput(consoleState, output);
        }
    }

    private static void WriteOutput(InteractiveConsoleState consoleState, AdminConsoleOutput output)
    {
        consoleState.WriteSegmentsLineDirect([.. output.Segments.Select(segment => new ConsoleTextSegment(
            segment.Text,
            segment.HasForegroundColor ? (ConsoleColor)segment.ForegroundColor : null,
            segment.HasBackgroundColor ? (ConsoleColor)segment.BackgroundColor : null))]);
    }

    private static bool IsExitCommand(string commandLine)
    {
        return string.Equals(commandLine.Trim(), "exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(commandLine.Trim(), "quit", StringComparison.OrdinalIgnoreCase);
    }
}
