using Microsoft.Extensions.Hosting;

namespace OhMyBot.Core.Terminal;

public sealed class InteractiveConsoleRendererHostedService(
    InteractiveConsoleState consoleState,
    InteractiveConsoleOutputQueue outputQueue) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!consoleState.Enabled)
        {
            return;
        }

        await foreach (var item in outputQueue.ReadAllAsync(stoppingToken))
        {
            consoleState.WriteSegmentsLineDirect(item.Segments);
        }
    }
}
