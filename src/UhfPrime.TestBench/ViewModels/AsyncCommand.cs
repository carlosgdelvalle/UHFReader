using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;

namespace UhfPrime.TestBench.ViewModels;

internal sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _executeAsync;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    public AsyncCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _ = ExecuteInternalAsync();
    }

    private async Task ExecuteInternalAsync()
    {
        _isExecuting = true;
        await RaiseCanExecuteChangedAsync().ConfigureAwait(false);

        try
        {
            await _executeAsync().ConfigureAwait(false);
        }
        finally
        {
            _isExecuting = false;
            await RaiseCanExecuteChangedAsync().ConfigureAwait(false);
        }
    }

    public void RaiseCanExecuteChanged() => _ = RaiseCanExecuteChangedAsync();

    private Task RaiseCanExecuteChangedAsync()
    {
        if (CanExecuteChanged is null)
        {
            return Task.CompletedTask;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            CanExecuteChanged.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty)).GetTask();
    }
}
