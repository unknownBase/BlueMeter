using BlueMeter.Services;
using Xunit;

namespace BlueMeter.Tests;

public sealed class SingleInstanceServiceTests
{
    [Fact]
    public async Task SecondInstance_CannotAcquire_AndActivatesFirst()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = $@"Local\BlueMeter.Tests.{suffix}";
        var pipeName = $"BlueMeter.Tests.{suffix}";

        using var owner = new SingleInstanceService(mutexName, pipeName);
        Assert.True(owner.TryAcquire());

        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.StartListening(() => activated.TrySetResult());

        // 给监听线程一点时间把管道服务器建起来，避免纯时序抖动。
        await Task.Delay(200);

        using var second = new SingleInstanceService(mutexName, pipeName);
        Assert.False(second.TryAcquire());

        var completed = await Task.WhenAny(activated.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(activated.Task, completed);
    }

    [Fact]
    public void AfterDispose_CanAcquireAgain()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = $@"Local\BlueMeter.Tests.{suffix}";
        var pipeName = $"BlueMeter.Tests.{suffix}";

        var first = new SingleInstanceService(mutexName, pipeName);
        Assert.True(first.TryAcquire());
        first.Dispose();

        using var second = new SingleInstanceService(mutexName, pipeName);
        Assert.True(second.TryAcquire());
    }

    [Fact]
    public void StartListening_WithoutOwnership_DoesNothing()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var service = new SingleInstanceService(
            $@"Local\BlueMeter.Tests.{suffix}",
            $"BlueMeter.Tests.{suffix}");

        service.StartListening(() => { });
    }
}
