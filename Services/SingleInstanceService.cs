using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace BlueMeter.Services;

/// <summary>
/// 单实例控制：命名互斥体保证只有一个进程存活，第二个实例通过命名管道
/// 请已有实例把主窗口显示出来，而不是静默退出。
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    public const string DefaultMutexName = @"Local\BlueMeter.SingleInstance";

    private const string ActivateMessage = "activate";
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ListenerStopTimeout = TimeSpan.FromSeconds(1);

    private readonly string _mutexName;
    private readonly string _pipeName;
    private Mutex? _mutex;
    private bool _ownsMutex;
    private int _ownerThreadId;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;
    private Action? _onActivate;
    private bool _disposed;

    /// <param name="mutexName">互斥体名称，默认按用户会话隔离。</param>
    /// <param name="pipeName">管道名称，默认按会话隔离，避免跨用户会话串话。</param>
    public SingleInstanceService(string? mutexName = null, string? pipeName = null)
    {
        _mutexName = mutexName ?? DefaultMutexName;
        _pipeName = pipeName ?? $"BlueMeter.Activate.{Process.GetCurrentProcess().SessionId}";
    }

    /// <summary>
    /// 尝试成为唯一实例。已有实例在运行时通知它显示主窗口并返回 false；
    /// 互斥体创建失败时也返回 false（此时不启动第二份托盘）。
    /// </summary>
    public bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);
            _ownsMutex = createdNew;

            if (createdNew)
            {
                _ownerThreadId = Environment.CurrentManagedThreadId;
                return true;
            }

            _mutex.Dispose();
            _mutex = null;
            SignalExistingInstance();
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Error("单实例互斥体创建失败", ex, "App");
            return false;
        }
    }

    /// <summary>
    /// 开始监听“显示主窗口”请求。回调在线程池线程上触发，调用方自行编组到 UI 线程。
    /// 只有持有互斥体的实例需要调用。
    /// </summary>
    public void StartListening(Action onActivate)
    {
        ArgumentNullException.ThrowIfNull(onActivate);

        if (_disposed || !_ownsMutex || _listenerTask is not null)
        {
            return;
        }

        _onActivate = onActivate;
        _listenerCts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenAsync(_listenerCts.Token));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _listenerCts?.Cancel();
            _listenerTask?.Wait(ListenerStopTimeout);
        }
        catch (Exception ex)
        {
            AppLog.Warn("停止单实例监听失败", "App", ex);
        }
        finally
        {
            _listenerCts?.Dispose();
            _listenerCts = null;
            _listenerTask = null;
            _onActivate = null;
            ReleaseMutex();
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                using var reader = new StreamReader(server);
                var request = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

                if (string.Equals(request, ActivateMessage, StringComparison.Ordinal))
                {
                    AppLog.Info("收到第二个实例的显示请求", "App");
                    _onActivate?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // 管道被占用或客户端中途断开时重试，避免监听线程整个退出。
                AppLog.Warn("单实例管道监听出错，稍后重试", "App", ex);
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private void SignalExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);

            client.Connect((int)SignalTimeout.TotalMilliseconds);

            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(ActivateMessage);
        }
        catch (Exception ex)
        {
            // 已有实例可能正在退出或尚未开始监听；此时按“直接退出”处理即可。
            AppLog.Warn("未能通知已运行的 BlueMeter 实例", "App", ex);
        }
    }

    private void ReleaseMutex()
    {
        if (_mutex is null)
        {
            return;
        }

        try
        {
            // 持有互斥体的是获取时的那个线程；跨线程调用 ReleaseMutex 会抛异常，
            // 此时交给进程退出释放即可，不当成错误记录。
            if (_ownsMutex && _ownerThreadId == Environment.CurrentManagedThreadId)
            {
                _mutex.ReleaseMutex();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("释放单实例互斥体失败", "App", ex);
        }
        finally
        {
            _mutex.Dispose();
            _mutex = null;
            _ownsMutex = false;
            _ownerThreadId = 0;
        }
    }
}
