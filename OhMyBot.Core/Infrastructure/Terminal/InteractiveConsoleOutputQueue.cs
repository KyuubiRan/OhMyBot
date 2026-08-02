using System.Threading.Channels;
using System.Runtime.CompilerServices;

namespace OhMyBot.Core.Infrastructure.Terminal;

public sealed class InteractiveConsoleOutputQueue
{
    private const int MaxPendingCount = 1024;
    private readonly Lock _gate = new();
    private readonly List<Channel<InteractiveConsoleOutputItem>> _readers = [];
    private readonly Queue<InteractiveConsoleOutputItem> _pending = new();

    public bool TryEnqueue(InteractiveConsoleOutputItem item)
    {
        Channel<InteractiveConsoleOutputItem>[] readers;
        lock (_gate)
        {
            readers = [.. _readers];
            if (readers.Length == 0)
            {
                if (_pending.Count >= MaxPendingCount)
                {
                    _pending.Dequeue();
                }

                _pending.Enqueue(item);
                return true;
            }
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
            while (_pending.TryDequeue(out var pending))
            {
                reader.Writer.TryWrite(pending);
            }
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
