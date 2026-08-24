using System.Threading.Channels;
using System.Runtime.CompilerServices;

namespace OhMyBot.Core.Infrastructure.Terminal;

public sealed class InteractiveConsoleOutputQueue
{
    private const int MaxRetainedCount = 1024;
    private readonly Lock _gate = new();
    private readonly List<InteractiveConsoleOutputSubscription> _subscriptions = [];
    private readonly Queue<HistoryEntry> _history = new();
    private long _lastSequence;

    public bool TryEnqueue(InteractiveConsoleOutputItem item)
    {
        InteractiveConsoleOutputSubscription[] subscriptions;
        lock (_gate)
        {
            _history.Enqueue(new HistoryEntry(++_lastSequence, item));
            if (_history.Count > MaxRetainedCount)
            {
                _history.Dequeue();
            }

            subscriptions = [.. _subscriptions];
        }

        foreach (var subscription in subscriptions)
        {
            subscription.TryWrite(item);
        }

        return true;
    }

    public InteractiveConsoleOutputSubscription Subscribe(int initialHistoryCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialHistoryCount);

        lock (_gate)
        {
            var initialHistory = _history
                .TakeLast(Math.Min(initialHistoryCount, MaxRetainedCount))
                .ToArray();
            var olderThanSequence = initialHistory.Length > 0
                ? initialHistory[0].Sequence
                : _lastSequence + 1;
            var subscription = new InteractiveConsoleOutputSubscription(
                this,
                initialHistory.Select(entry => entry.Item).ToArray(),
                olderThanSequence);
            _subscriptions.Add(subscription);
            return subscription;
        }
    }

    public async IAsyncEnumerable<InteractiveConsoleOutputItem> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var subscription = Subscribe(MaxRetainedCount);
        foreach (var item in subscription.InitialHistory)
        {
            yield return item;
        }

        await foreach (var item in subscription.ReadLiveAsync(cancellationToken))
        {
            yield return item;
        }
    }

    internal IReadOnlyList<InteractiveConsoleOutputItem> ReadOlder(
        InteractiveConsoleOutputSubscription subscription,
        int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        lock (_gate)
        {
            if (!_subscriptions.Contains(subscription))
            {
                return [];
            }

            var page = _history
                .Where(entry => entry.Sequence < subscription.OlderThanSequence)
                .TakeLast(Math.Min(count, MaxRetainedCount))
                .ToArray();
            if (page.Length > 0)
            {
                subscription.OlderThanSequence = page[0].Sequence;
            }

            return page.Select(entry => entry.Item).ToArray();
        }
    }

    internal void Unsubscribe(InteractiveConsoleOutputSubscription subscription)
    {
        lock (_gate)
        {
            _subscriptions.Remove(subscription);
        }
    }

    private sealed record HistoryEntry(long Sequence, InteractiveConsoleOutputItem Item);
}

public sealed record InteractiveConsoleOutputItem(IReadOnlyList<ConsoleTextSegment> Segments);

public sealed class InteractiveConsoleOutputSubscription : IAsyncDisposable
{
    private readonly InteractiveConsoleOutputQueue _owner;
    private readonly Channel<InteractiveConsoleOutputItem> _liveItems = Channel.CreateBounded<InteractiveConsoleOutputItem>(
        new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    private int _disposed;

    internal InteractiveConsoleOutputSubscription(
        InteractiveConsoleOutputQueue owner,
        IReadOnlyList<InteractiveConsoleOutputItem> initialHistory,
        long olderThanSequence)
    {
        _owner = owner;
        InitialHistory = initialHistory;
        OlderThanSequence = olderThanSequence;
    }

    public IReadOnlyList<InteractiveConsoleOutputItem> InitialHistory { get; }

    internal long OlderThanSequence { get; set; }

    public IReadOnlyList<InteractiveConsoleOutputItem> ReadOlder(int count)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _owner.ReadOlder(this, count);
    }

    public IAsyncEnumerable<InteractiveConsoleOutputItem> ReadLiveAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _liveItems.Reader.ReadAllAsync(cancellationToken);
    }

    internal bool TryWrite(InteractiveConsoleOutputItem item)
    {
        return _liveItems.Writer.TryWrite(item);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _owner.Unsubscribe(this);
            _liveItems.Writer.TryComplete();
        }

        return ValueTask.CompletedTask;
    }
}
