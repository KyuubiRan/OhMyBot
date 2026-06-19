using OhMyBot.Core.Admin;

namespace OhMyBot.Core.Terminal;

public sealed class InteractiveConsoleHostedService(
    InteractiveConsoleState consoleState,
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime applicationLifetime,
    ILogger<InteractiveConsoleHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!consoleState.Enabled)
        {
            logger.LogDebug("Interactive console is disabled because stdin or stdout is redirected.");
            return;
        }

        consoleState.WriteLine("OhMyBot Core interactive console is ready. Type 'help' for available commands.");
        consoleState.RenderPrompt();

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!Console.KeyAvailable)
            {
                await Task.Delay(50, stoppingToken);
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

                if (IsExitCommand(commandLine))
                {
                    consoleState.WriteLine("Stopping OhMyBot Core...");
                    applicationLifetime.StopApplication();
                    return;
                }

                await ExecuteCommandAsync(commandLine, stoppingToken);
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

    private async Task ExecuteCommandAsync(string commandLine, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var executor = scope.ServiceProvider.GetRequiredService<AdminCommandExecutor>();
            var result = await executor.ExecuteAsync(commandLine, cancellationToken);
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                consoleState.WriteLine(result.Success ? result.Message : $"Error: {result.Message}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to execute interactive console command.");
        }
    }

    private static bool IsExitCommand(string commandLine)
    {
        return string.Equals(commandLine, "exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(commandLine, "quit", StringComparison.OrdinalIgnoreCase);
    }
}
