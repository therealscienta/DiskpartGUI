using DiskpartGUI.ViewModels.Infrastructure;
using Xunit;

namespace DiskpartGUI.Tests.Infrastructure;

public sealed class AsyncRelayCommandTests
{
    [Fact]
    public async Task Execute_CallsAsyncAction()
    {
        var called = false;
        var cmd = new AsyncRelayCommand(async ct =>
        {
            await Task.Delay(1, ct);
            called = true;
        });

        cmd.Execute(null);
        await Task.Delay(50); // give async operation time to complete

        Assert.True(called);
    }

    [Fact]
    public void CanExecute_NoCanExecutePredicate_ReturnsTrue()
    {
        var cmd = new AsyncRelayCommand(ct => Task.CompletedTask);
        Assert.True(cmd.CanExecute(null));
    }

    [Fact]
    public void CanExecute_PredicateReturnsFalse_ReturnsFalse()
    {
        var cmd = new AsyncRelayCommand(ct => Task.CompletedTask, canExecute: () => false);
        Assert.False(cmd.CanExecute(null));
    }

    [Fact]
    public void Constructor_NullExecute_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AsyncRelayCommand((Func<CancellationToken, Task>)null!));
    }

    [Fact]
    public async Task Execute_OnException_CallsErrorHandler()
    {
        Exception? caught = null;
        var cmd = new AsyncRelayCommand(
            ct => throw new InvalidOperationException("test error"),
            onError: ex => caught = ex);

        cmd.Execute(null);
        await Task.Delay(50);

        Assert.NotNull(caught);
        Assert.IsType<InvalidOperationException>(caught);
    }

    [Fact]
    public async Task Execute_WhileRunning_CanExecuteReturnsFalse()
    {
        var tcs = new TaskCompletionSource<bool>();
        bool? canExecuteDuringRun = null;

        AsyncRelayCommand? cmd = null;
        cmd = new AsyncRelayCommand(async ct =>
        {
            canExecuteDuringRun = cmd!.CanExecute(null);
            await tcs.Task;
        });

        cmd.Execute(null);
        await Task.Delay(20); // let it start

        Assert.False(canExecuteDuringRun);

        tcs.SetResult(true);
        await Task.Delay(20); // let it finish

        Assert.True(cmd.CanExecute(null));
    }
}
