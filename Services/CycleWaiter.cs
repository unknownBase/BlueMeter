namespace BlueMeter.Services;

/// <summary>
/// 可中断的轮次等待。刷新间隔改动时打断当前等待并按新间隔重新计时：
/// 不必等满旧间隔，也不会因为打断而立刻触发一轮刷新。进程退出时原样抛出取消异常。
/// </summary>
public sealed class CycleWaiter
{
    private readonly object _lock = new();
    private readonly Func<TimeSpan> _nextDelay;
    private CancellationTokenSource? _waitCts;
    private bool _interruptPending;

    public CycleWaiter(Func<TimeSpan> nextDelay)
    {
        _nextDelay = nextDelay ?? throw new ArgumentNullException(nameof(nextDelay));
    }

    /// <summary>是否有等待正在进行。没有等待（正在刷新）时打断会被忽略，新间隔在下一次等待时自然生效。</summary>
    public bool IsWaiting
    {
        get
        {
            lock (_lock)
            {
                return _waitCts is not null;
            }
        }
    }

    /// <summary>
    /// 等到下一轮。返回 true 表示中途被打断过、已按新间隔重新计时到点；返回 false 表示正常等到。
    /// </summary>
    public async Task<bool> WaitAsync(CancellationToken cancellationToken)
    {
        var interrupted = false;
        while (true)
        {
            var delay = _nextDelay();

            // 每次等待独立一个令牌源：打断只影响当前这一段，不会污染进程级取消。
            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (_lock)
            {
                _waitCts = waitCts;
            }

            try
            {
                bool restart;
                lock (_lock)
                {
                    _waitCts = waitCts;
                    // 打断可能落在“算完间隔、还没登记”的瞬间，这里补一次重新计时。
                    restart = _interruptPending;
                    _interruptPending = false;
                }

                if (restart)
                {
                    interrupted = true;
                    continue;
                }

                await Task.Delay(delay, waitCts.Token).ConfigureAwait(false);
                return interrupted;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                interrupted = true;
            }
            finally
            {
                lock (_lock)
                {
                    if (ReferenceEquals(_waitCts, waitCts))
                    {
                        _waitCts = null;
                    }
                }
            }
        }
    }

    /// <summary>打断当前等待，让调用方按新间隔重新计时；没有等待时什么都不做。</summary>
    public void Interrupt()
    {
        CancellationTokenSource? waitCts;
        lock (_lock)
        {
            waitCts = _waitCts;
            if (waitCts is null)
            {
                // 没有等待可打断：记下来，下一次等待登记后立即按新间隔重新计时。
                _interruptPending = true;
                return;
            }
        }

        try
        {
            waitCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 等待刚到点并释放：新间隔会在下一次等待时自然生效。
        }
    }
}
