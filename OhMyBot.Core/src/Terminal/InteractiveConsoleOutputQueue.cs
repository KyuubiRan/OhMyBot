using System.Threading.Channels;
using System.Runtime.CompilerServices;

namespace OhMyBot.Core.Terminal;

public sealed class InteractiveConsoleOutputQueue
{
    private readonly Lock _gate = new();
    private readonly List<Channel<InteractiveConsoleOutputItem>> _readers = [];

    public bool TryEnqueue(InteractiveConsoleOutputItem item)
    {
        Channel<InteractiveConsoleOutputItem>[] readers;
        lock (_gate)
        {
            readers = [.. _readers];
        }

        foreach (var reader in readers)
        {
            reader.Writer.TryWrite(item);
        }

        return readers.Length > 0;
    }

    public async IAsyncEnumerable<InteractiveConsoleOutputItem> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = Channel.CreateBounded<InteractiveConsoleOutputItem>(
            new BoundedChannelOptions(1024)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false
            });

        lock (_gate)
        {
            _readers.Add(reader);
        }

        try
        {
            await foreach (var item in reader.Reader.ReadAllAsync(cancellationToken))
            {
                yield return item;
            }
        }
        finally
        {
            lock (_gate)
            {
                _readers.Remove(reader);
            }

            reader.Writer.TryComplete();
        }
    }
}

public sealed record InteractiveConsoleOutputItem(IReadOnlyList<ConsoleTextSegment> Segments);
