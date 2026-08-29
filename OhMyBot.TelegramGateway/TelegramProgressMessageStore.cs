namespace OhMyBot.TelegramGateway;

public sealed class TelegramProgressMessageStore
{
    private static readonly TimeSpan CompletedKeyLifetime = TimeSpan.FromMinutes(10);
    private readonly object _gate = new();
    private readonly Dictionary<string, TaskCompletionSource<int>> _messages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _completed = new(StringComparer.Ordinal);
    private long _nextPruneUtcTicks;

    public bool IsCompleted(string messageKey)
    {
        lock (_gate)
        {
            return IsCompletedCore(messageKey);
        }
    }

    private bool IsCompletedCore(string messageKey)
    {
        if (!_completed.TryGetValue(messageKey, out var completedAt))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow - completedAt <= CompletedKeyLifetime)
        {
            return true;
        }

        _completed.Remove(messageKey);
        return false;
    }

    public bool TryGet(string messageKey, out int messageId)
    {
        lock (_gate)
        {
            return TryGetCore(messageKey, out messageId);
        }
    }

    private bool TryGetCore(string messageKey, out int messageId)
    {
        messageId = 0;
        return _messages.TryGetValue(messageKey, out var source)
            && source.Task.IsCompletedSuccessfully
            && (messageId = source.Task.Result) > 0;
    }

    public bool Register(string messageKey, int messageId)
    {
        lock (_gate)
        {
            if (IsCompletedCore(messageKey))
            {
                return false;
            }

            if (!_messages.TryGetValue(messageKey, out var source))
            {
                source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                _messages.Add(messageKey, source);
            }

            return source.TrySetResult(messageId);
        }
    }

    public async Task<int?> WaitForAsync(
        string messageKey,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<int> source;
        lock (_gate)
        {
            if (IsCompletedCore(messageKey))
            {
                return null;
            }

            if (TryGetCore(messageKey, out var messageId))
            {
                return messageId;
            }

            if (!_messages.TryGetValue(messageKey, out source!))
            {
                source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                _messages.Add(messageKey, source);
            }
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            return await source.Task.WaitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public void Complete(string messageKey)
    {
        lock (_gate)
        {
            _messages.Remove(messageKey);
            var now = DateTimeOffset.UtcNow;
            _completed[messageKey] = now;
            PruneCompleted(now);
        }
    }

    private void PruneCompleted(DateTimeOffset now)
    {
        if (now.UtcTicks < _nextPruneUtcTicks)
        {
            return;
        }

        _nextPruneUtcTicks = now.AddMinutes(1).UtcTicks;
        var cutoff = now - CompletedKeyLifetime;
        foreach (var key in _completed
                     .Where(item => item.Value < cutoff)
                     .Select(item => item.Key)
                     .ToArray())
        {
            _completed.Remove(key);
        }
    }
}
