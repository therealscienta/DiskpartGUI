using DiskpartGUI.ViewModels.Infrastructure;
using Xunit;

namespace DiskpartGUI.Tests.Infrastructure;

public sealed class RelayCommandTests
{
    [Fact]
    public void Execute_CallsProvidedAction()
    {
        var called = false;
        var cmd = new RelayCommand(() => called = true);
        cmd.Execute(null);
        Assert.True(called);
    }

    [Fact]
    public void CanExecute_NoCanExecutePredicate_ReturnsTrue()
    {
        var cmd = new RelayCommand(() => { });
        Assert.True(cmd.CanExecute(null));
    }

    [Fact]
    public void CanExecute_PredicateReturnsFalse_ReturnsFalse()
    {
        var cmd = new RelayCommand(() => { }, canExecute: () => false);
        Assert.False(cmd.CanExecute(null));
    }

    [Fact]
    public void CanExecute_PredicateReturnsTrue_ReturnsTrue()
    {
        var cmd = new RelayCommand(() => { }, canExecute: () => true);
        Assert.True(cmd.CanExecute(null));
    }

    [Fact]
    public void RaiseCanExecuteChanged_FiresEvent()
    {
        var cmd = new RelayCommand(() => { });
        var fired = false;
        cmd.CanExecuteChanged += (_, _) => fired = true;

        cmd.RaiseCanExecuteChanged();

        Assert.True(fired);
    }

    [Fact]
    public void Constructor_NullExecute_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RelayCommand((Action)null!));
    }
}
