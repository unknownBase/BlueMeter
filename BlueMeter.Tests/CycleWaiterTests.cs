using BlueMeter.Services;
using Xunit;

namespace BlueMeter.Tests;

public sealed class CycleWaiterTests
{
    [Fact]
    public async Task WaitAsync_ReturnsFalse_WhenDelayElapses()
    {
        var waiter = new CycleWaiter(() => TimeSpan.FromMilliseconds(1));

        var interrupted = await waiter.WaitAsync(CancellationToken.None);

        Assert.False(interrupted);
        Assert.False(waiter.IsWaiting);
    }

    [Fact]
    public async Task Interrupt_RestartsWithNewDelay()
    {
        var calls = 0;
        var waiter = new CycleWaiter(() =>
        {
            // 第一段故意设很长，靠打断把它换成第二段的短间隔。
            var index = Interlocked.Increment(ref calls);
            return index == 1
                ? TimeSpan.FromSeconds(30)
                : TimeSpan.FromMilliseconds(10);
        });

        var waitTask = waiter.WaitAsync(CancellationToken.None);
        await WaitUntilWaitingAsync(waiter);

        waiter.Interrupt();

        // 若没有重新计时，这里会等满 30 秒并超时。
        var interrupted = await waitTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(interrupted);
        Assert.True(calls >= 2);
        Assert.False(waiter.IsWaiting);
    }

    [Fact]
    public async Task WaitAsync_PropagatesCancellation()
    {
        var waiter = new CycleWaiter(() => TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => waiter.WaitAsync(cts.Token));
    }

    [Fact]
    public async Task InterruptBeforeWait_RestartsWithFreshDelay()
    {
        // 打断落在“算完间隔、还没登记”的间隙时，不能丢掉这次重新计时。
        var calls = 0;
        var waiter = new CycleWaiter(() =>
        {
            var index = Interlocked.Increment(ref calls);
            return index == 1
                ? TimeSpan.FromSeconds(30)
                : TimeSpan.FromMilliseconds(10);
        });

        waiter.Interrupt();

        var interrupted = await waiter.WaitAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(interrupted);
        Assert.False(waiter.IsWaiting);
    }

    [Fact]
    public void Interrupt_WithoutWait_DoesNotThrow()
    {
        var waiter = new CycleWaiter(() => TimeSpan.FromMilliseconds(1));

        waiter.Interrupt();

        Assert.False(waiter.IsWaiting);
    }

    private static async Task WaitUntilWaitingAsync(CycleWaiter waiter)
    {
        for (var attempt = 0; attempt < 200 && !waiter.IsWaiting; attempt++)
        {
            await Task.Delay(5);
        }

        Assert.True(waiter.IsWaiting);
    }
}
