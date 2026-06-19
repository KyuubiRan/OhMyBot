using System.Threading.Channels;

namespace OhMyBot.Core.Terminal;

public sealed class InteractiveConsoleOutputQueue
{
    private readonly Channel<InteractiveConsoleOutputItem> _queue = Channel.CreateBounded<InteractiveConsoleOutputItem>(
        new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

    public bool TryEnqueue(InteractiveConsoleOutputItem item)
    {
        return _queue.Writer.TryWrite(item);
    }

    public IAsyncEnumerable<InteractiveConsoleOutputItem> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _queue.Reader.ReadAllAsync(cancellationToken);
    }
}

public sealed record InteractiveConsoleOutputItem(IReadOnlyList<ConsoleTextSegment> Segments);
