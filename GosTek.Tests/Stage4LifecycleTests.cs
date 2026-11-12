/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты для Этапа 4: машина состояний камеры, NavigationGuard, ReduceMotion
 * СОДЕРЖИМОЕ: Stage4LifecycleTests
 * ЗАВИСИМОСТИ: GosTek.Services, GosTek.Helpers, Xunit
 * ==============================================================================
 */

using GosTek.Helpers;
using GosTek.Services;
using Xunit;

namespace GosTek.Tests;

public class Stage4LifecycleTests
{
    [Fact]
    public void CameraLifecycle_InitialState_IsStopped()
    {
        var controller = new CameraLifecycleController();
        Assert.Equal(CameraLifecycleState.Stopped, controller.State);
        Assert.True(controller.IsStopped);
        Assert.False(controller.IsStarting);
        Assert.False(controller.IsRunning);
    }

    [Fact]
    public void CameraLifecycle_TryBeginStart_TransitionsToStarting()
    {
        var controller = new CameraLifecycleController();
        bool started = controller.TryBeginStart(1);

        Assert.True(started);
        Assert.Equal(CameraLifecycleState.Starting, controller.State);
        Assert.True(controller.IsStarting);
    }

    [Fact]
    public void CameraLifecycle_ConcurrentStart_SecondFails()
    {
        var controller = new CameraLifecycleController();
        bool first = controller.TryBeginStart(1);
        bool second = controller.TryBeginStart(2);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(CameraLifecycleState.Starting, controller.State);
    }

    [Fact]
    public void CameraLifecycle_CompleteStart_Success_WhenSameToken()
    {
        var controller = new CameraLifecycleController();
        controller.TryBeginStart(42);
        bool completed = controller.CompleteStart(42);

        Assert.True(completed);
        Assert.Equal(CameraLifecycleState.Running, controller.State);
        Assert.True(controller.IsRunning);
    }

    [Fact]
    public void CameraLifecycle_CompleteStart_Fails_WhenTokenMismatch()
    {
        var controller = new CameraLifecycleController();
        controller.TryBeginStart(42);
        bool completed = controller.CompleteStart(99);

        Assert.False(completed);
        Assert.Equal(CameraLifecycleState.Starting, controller.State);
    }

    [Fact]
    public void CameraLifecycle_CompleteStart_Fails_WhenStoppedBeforeCompletion()
    {
        var controller = new CameraLifecycleController();
        controller.TryBeginStart(42);
        controller.Stop();
        bool completed = controller.CompleteStart(42);

        Assert.False(completed);
        Assert.Equal(CameraLifecycleState.Stopped, controller.State);
        Assert.True(controller.IsStopped);
    }

    [Fact]
    public void CameraLifecycle_Stop_ResetsToStopped()
    {
        var controller = new CameraLifecycleController();
        controller.TryBeginStart(1);
        controller.CompleteStart(1);
        Assert.True(controller.IsRunning);

        controller.Stop();
        Assert.Equal(CameraLifecycleState.Stopped, controller.State);
        Assert.True(controller.IsStopped);
    }

    [Fact]
    public void CameraLifecycle_Dispose_PreventsFurtherStarts()
    {
        var controller = new CameraLifecycleController();
        controller.Dispose();

        Assert.True(controller.IsDisposed);
        bool started = controller.TryBeginStart(1);
        Assert.False(started);
        Assert.Equal(CameraLifecycleState.Disposed, controller.State);
    }

    [Fact]
    public async Task NavigationGuard_AllowsSingleNavigation()
    {
        NavigationGuard.ResetForTesting();
        bool executed = false;

        bool result = await NavigationGuard.NavigateAsync(async () =>
        {
            await Task.Yield();
            executed = true;
        }, debounceMs: 0);

        Assert.True(result);
        Assert.True(executed);
    }

    [Fact]
    public async Task NavigationGuard_DebouncesConcurrentNavigations()
    {
        NavigationGuard.ResetForTesting();
        var tcs = new TaskCompletionSource<bool>();
        int executedCount = 0;

        // Первый переход блокирует выполнение
        var firstTask = NavigationGuard.NavigateAsync(async () =>
        {
            Interlocked.Increment(ref executedCount);
            await tcs.Task;
        }, debounceMs: 50);

        // Второй переход во время первого должен быть отклонен guard-ом
        bool secondResult = await NavigationGuard.NavigateAsync(async () =>
        {
            Interlocked.Increment(ref executedCount);
            await Task.Yield();
        }, debounceMs: 0);

        Assert.False(secondResult);

        // Разблокируем первый переход
        tcs.SetResult(true);
        bool firstResult = await firstTask;

        Assert.True(firstResult);
        Assert.Equal(1, executedCount);
    }

    [Fact]
    public void AnimationHelper_ReduceMotion_FlagPersists()
    {
        AnimationHelper.ReduceMotionEnabled = false;
        Assert.False(AnimationHelper.ReduceMotionEnabled);

        AnimationHelper.ReduceMotionEnabled = true;
        Assert.True(AnimationHelper.ReduceMotionEnabled);

        AnimationHelper.ReduceMotionEnabled = false;
    }
}
