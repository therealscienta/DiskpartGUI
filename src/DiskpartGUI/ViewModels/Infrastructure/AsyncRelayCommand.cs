using System.Windows.Input;

namespace DiskpartGUI.ViewModels.Infrastructure;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, CancellationToken, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private readonly SynchronizationContext? _syncContext;
    private bool _isExecuting;

    public AsyncRelayCommand(
        Func<CancellationToken, Task> execute,
        Func<bool>? canExecute = null,
        Action<Exception>? onError = null)
        : this(WrapExecute(execute), canExecute is null ? null : _ => canExecute(), onError)
    { }

    private static Func<object?, CancellationToken, Task> WrapExecute(Func<CancellationToken, Task> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return (_, ct) => execute(ct);
    }

    public AsyncRelayCommand(
        Func<object?, CancellationToken, Task> execute,
        Func<object?, bool>? canExecute = null,
        Action<Exception>? onError = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _onError = onError;
        _syncContext = SynchronizationContext.Current;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
        => !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync(parameter);

    public async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        SetIsExecuting(true);
        try
        {
            await _execute(parameter, CancellationToken.None);
        }
        catch (Exception ex) when (_onError is not null)
        {
            _onError(ex);
        }
        finally
        {
            SetIsExecuting(false);
        }
    }

    public void RaiseCanExecuteChanged()
    {
        if (_syncContext is not null)
            _syncContext.Post(_ => CanExecuteChanged?.Invoke(this, EventArgs.Empty), null);
        else
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetIsExecuting(bool value)
    {
        _isExecuting = value;
        RaiseCanExecuteChanged();
    }
}
