using Foldcraft.App.Mvvm;

namespace Foldcraft.App.ViewModels;

/// <summary>
/// The one piece of long-running work allowed at a time, and the status bar that shows it.
/// Apply and restore both change folders, so they share this and can never overlap.
/// </summary>
public sealed class ActivityViewModel : ObservableObject
{
    private CancellationTokenSource? _cancellation;
    private bool _isBusy;
    private string _message = "Ready.";
    private int _total;
    private int _completed;
    private bool _canCancel;

    public ActivityViewModel()
    {
        CancelCommand = new RelayCommand(Cancel, () => CanCancel);
    }

    public event EventHandler? BusyChanged;

    public RelayCommand CancelCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                BusyChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    public int Total
    {
        get => _total;
        private set
        {
            if (Set(ref _total, value))
            {
                OnPropertyChanged(nameof(IsIndeterminate));
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    public int Completed
    {
        get => _completed;
        private set
        {
            if (Set(ref _completed, value))
            {
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    public bool IsIndeterminate => Total <= 0;

    public string ProgressText => Total > 0 ? $"{Completed} of {Total}" : string.Empty;

    public bool CanCancel
    {
        get => _canCancel;
        private set
        {
            if (Set(ref _canCancel, value))
            {
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Starts a piece of work. The token fires if the user cancels.</summary>
    /// <exception cref="InvalidOperationException">Other work is still running.</exception>
    public CancellationToken Begin(string message, int total = 0, bool cancellable = false)
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("Foldcraft is already busy.");
        }

        _cancellation = new CancellationTokenSource();
        Message = message;
        Total = total;
        Completed = 0;
        CanCancel = cancellable;
        IsBusy = true;
        return _cancellation.Token;
    }

    public void Advance() => Completed++;

    /// <summary>The same work is starting over on a smaller set, so the count starts over too.</summary>
    public void Restart(string message, int total)
    {
        Completed = 0;
        Total = total;
        Message = message;
    }

    /// <summary>Puts a note on the status bar, unless work is using it.</summary>
    public void ShowMessage(string message)
    {
        if (!IsBusy)
        {
            Message = message;
        }
    }

    public void Finish(string summary)
    {
        _cancellation?.Dispose();
        _cancellation = null;
        CanCancel = false;
        Total = 0;
        Completed = 0;
        Message = summary;
        IsBusy = false;
    }

    public void Cancel()
    {
        if (CanCancel)
        {
            _cancellation?.Cancel();
            CanCancel = false;
            Message = "Stopping after the current folder...";
        }
    }
}
