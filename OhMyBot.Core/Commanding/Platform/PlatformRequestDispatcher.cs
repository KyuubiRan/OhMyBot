namespace OhMyBot.Core.Commanding.Platform;

/// <summary>
/// 平台待审批请求的监听者。插件用 hosted service 在启动时订阅、停止时退订
/// （见 <see cref="PlatformRequestDispatcher"/>），因此不需要新的插件组件类型。
/// </summary>
public interface IPlatformRequestListener
{
    Task OnPlatformRequestAsync(PlatformRequestNotice notice, CancellationToken cancellationToken = default);
}

/// <summary>
/// 把网关上报的待审批请求分发给已订阅的插件。单例，订阅关系随插件生命周期增删。
/// 单个监听者抛异常不影响其它监听者，也不影响网关上报（网关只关心「有没有人受理」）。
/// </summary>
public sealed class PlatformRequestDispatcher(ILogger<PlatformRequestDispatcher> logger)
{
    private readonly Lock _lock = new();
    private IReadOnlyList<IPlatformRequestListener> _listeners = [];

    public IDisposable Subscribe(IPlatformRequestListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        lock (_lock)
        {
            _listeners = [.. _listeners, listener];
        }

        return new Subscription(this, listener);
    }

    /// <returns>是否有监听者受理。</returns>
    public async Task<bool> DispatchAsync(PlatformRequestNotice notice, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notice);
        IReadOnlyList<IPlatformRequestListener> listeners;
        lock (_lock)
        {
            listeners = _listeners;
        }

        if (listeners.Count == 0)
        {
            return false;
        }

        foreach (var listener in listeners)
        {
            try
            {
                await listener.OnPlatformRequestAsync(notice, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "平台请求监听者 {Listener} 处理失败。kind={Kind} flag={Flag}",
                    listener.GetType().Name,
                    notice.Kind,
                    notice.Flag);
            }
        }

        return true;
    }

    private void Unsubscribe(IPlatformRequestListener listener)
    {
        lock (_lock)
        {
            _listeners = _listeners.Where(item => !ReferenceEquals(item, listener)).ToArray();
        }
    }

    private sealed class Subscription(PlatformRequestDispatcher dispatcher, IPlatformRequestListener listener) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            dispatcher.Unsubscribe(listener);
        }
    }
}
