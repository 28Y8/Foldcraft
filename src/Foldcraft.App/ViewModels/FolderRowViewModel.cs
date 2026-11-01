using Foldcraft.App.Mvvm;
using Foldcraft.App.Services;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Icons;
using Foldcraft.Core.Safety;

namespace Foldcraft.App.ViewModels;

/// <summary>How a status should be drawn. It always comes with words, never color alone.</summary>
public enum StatusKind
{
    Neutral,
    Success,
    Warning,
    Error,
}

/// <summary>One folder in the list, and what has happened to it.</summary>
public sealed class FolderRowViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isWorking;
    private bool _isAllowed;
    private string _status = string.Empty;
    private StatusKind _kind;
    private FolderIconSpec? _appliedSpec;

    public FolderRowViewModel(FolderSafetyResult verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        Path = verdict.ResolvedPath ?? verdict.NormalizedPath ?? verdict.RequestedPath;
        var name = System.IO.Path.GetFileName(Path.TrimEnd('\\'));
        Name = string.IsNullOrEmpty(name) ? Path : name;
        _isAllowed = verdict.IsAllowed;

        if (_isAllowed)
        {
            ShowReady();
        }
        else
        {
            _kind = StatusKind.Error;
            _status = verdict.Refusal is { } refusal ? PlainEnglish.Refusal(refusal) : "Foldcraft can't safely change this folder.";
        }
    }

    public string Path { get; }

    public string Name { get; }

    public bool IsAllowed
    {
        get => _isAllowed;
        private set => Set(ref _isAllowed, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public bool IsWorking
    {
        get => _isWorking;
        private set => Set(ref _isWorking, value);
    }

    public string Status
    {
        get => _status;
        private set
        {
            if (Set(ref _status, value))
            {
                OnPropertyChanged(nameof(AccessibleText));
            }
        }
    }

    public StatusKind Kind
    {
        get => _kind;
        private set => Set(ref _kind, value);
    }

    /// <summary>What a screen reader announces for the row.</summary>
    public string AccessibleText => $"{Name}. {Path}. {Status}";

    /// <summary>True if applying this spec would be worth doing.</summary>
    public bool WillChange(FolderIconSpec spec) => IsAllowed && _appliedSpec != spec;

    public void BeginWork()
    {
        IsWorking = true;
        Status = "Working...";
        Kind = StatusKind.Neutral;
    }

    /// <summary>Safe to call twice with one result: live progress and the final report overlap.</summary>
    public void ShowResult(FolderApplyResult result, FolderIconSpec spec)
    {
        ArgumentNullException.ThrowIfNull(result);

        IsWorking = false;
        Status = PlainEnglish.ApplyResult(result);

        switch (result.Outcome)
        {
            case ApplyOutcome.Applied:
            case ApplyOutcome.Unchanged:
                _appliedSpec = spec;
                Kind = PlainEnglish.MayNotShowInExplorer(Path) ? StatusKind.Warning : StatusKind.Success;
                break;
            case ApplyOutcome.Canceled:
                Kind = StatusKind.Neutral;
                break;
            case ApplyOutcome.Refused:
                IsAllowed = false;
                Kind = StatusKind.Error;
                break;
            default:
                Kind = StatusKind.Error;
                break;
        }
    }

    /// <summary>The design moved on, so a finished row has work to do again.</summary>
    public void DesignChanged(FolderIconSpec spec)
    {
        if (_appliedSpec is not null && _appliedSpec != spec)
        {
            ShowReady();
        }
    }

    /// <summary>The folder may have been changed or put back, so what this row remembers no longer holds.</summary>
    public void ForgetAppliedLook()
    {
        if (_appliedSpec is not null)
        {
            _appliedSpec = null;
            ShowReady();
        }
    }

    private void ShowReady()
    {
        Kind = PlainEnglish.MayNotShowInExplorer(Path) ? StatusKind.Warning : StatusKind.Neutral;
        Status = Kind == StatusKind.Warning
            ? "Ready, but this location is very long, so Windows may not show the new look."
            : "Ready.";
    }
}
