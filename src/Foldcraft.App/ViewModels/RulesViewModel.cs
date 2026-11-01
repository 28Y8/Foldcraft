using System.Collections.ObjectModel;
using Foldcraft.App.Mvvm;
using Foldcraft.App.Services;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Rules;

namespace Foldcraft.App.ViewModels;

/// <summary>One kind of rule in the list of choices.</summary>
public sealed record RuleConditionChoice(string Name, RuleCondition Condition, string Hint);

/// <summary>One theme a rule can use.</summary>
public sealed record RuleThemeChoice(string Id, string Name);

/// <summary>One saved rule.</summary>
public sealed record RuleRowViewModel(FolderRule Rule, string Summary, RelayCommand RemoveCommand)
{
    public string RemoveName => $"Remove rule: {Summary}";
}

/// <summary>One line of the preview. Only lines that will change can be switched off.</summary>
public sealed class RulePreviewRowViewModel : ObservableObject
{
    private bool _isIncluded;

    public RulePreviewRowViewModel(RulePreviewRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        Row = row;
        Path = row.FolderPath;
        Message = PlainEnglish.RulePreview(row);
        CanInclude = row.Status == RulePreviewStatus.WillChange;
        _isIncluded = CanInclude;
        Kind = row.Status switch
        {
            RulePreviewStatus.WillChange => StatusKind.Neutral,
            RulePreviewStatus.AlreadyHasLook => StatusKind.Success,
            _ => StatusKind.Warning,
        };
    }

    public RulePreviewRow Row { get; }

    public string Path { get; }

    public string Name => System.IO.Path.GetFileName(Path) is { Length: > 0 } name ? name : Path;

    public string Message { get; }

    public bool CanInclude { get; }

    public StatusKind Kind { get; }

    public bool IsIncluded
    {
        get => _isIncluded;
        set => Set(ref _isIncluded, value);
    }
}

/// <summary>
/// The Rules screen. Rules never run by themselves: the user presses Preview, reads the list
/// and presses Apply. There's no background process behind this screen, and nothing runs
/// when the app starts.
/// </summary>
public sealed class RulesViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private readonly ActivityViewModel _activity;
    private readonly List<FolderRule> _rules = [];
    private RuleConditionChoice _selectedCondition;
    private RuleThemeChoice? _selectedTheme;
    private string _patternText = string.Empty;
    private string? _patternMessage;
    private string? _scopePath;
    private int _depth = 2;
    private string? _previewSummary;
    private string? _notice;

    public RulesViewModel(AppServices services, IDialogs dialogs, ActivityViewModel activity, Action<Exception> onError)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        ArgumentNullException.ThrowIfNull(onError);

        Conditions =
        [
            new("Name contains", RuleCondition.NameContains, "Part of a folder's name, like photos."),
            new("Name is exactly", RuleCondition.NameEquals, "A folder's whole name, like Invoices."),
            new("Name starts with", RuleCondition.NameStartsWith, "The start of a folder's name, like 2024."),
            new("Name fits a pattern", RuleCondition.NameWildcard, "Use * for any text and ? for one character, like Project *."),
            new("Mostly holds a file type", RuleCondition.MostlyFileType,
                "One of: pictures, music, videos, documents, code or archives. Or one extension, like .psd."),
        ];

        _selectedCondition = Conditions[0];

        AddRuleCommand = new RelayCommand(AddRule);
        PickScopeCommand = new RelayCommand(PickScope, () => !_activity.IsBusy);
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, onError, () => !_activity.IsBusy && ScopePath is not null && Rules.Count > 0);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, onError, () => !_activity.IsBusy && PreviewRows.Any(row => row.IsIncluded));

        _activity.BusyChanged += (_, _) => RefreshCommands();
    }

    /// <summary>Raised after folders changed, so the History screen can catch up.</summary>
    public event EventHandler? Applied;

    public IReadOnlyList<RuleConditionChoice> Conditions { get; }

    public IReadOnlyList<int> DepthChoices { get; } =
        Enumerable.Range(RuleScanner.MinDepth, RuleScanner.MaxDepth).ToArray();

    public ObservableCollection<RuleThemeChoice> Themes { get; } = [];

    public ObservableCollection<RuleRowViewModel> Rules { get; } = [];

    public ObservableCollection<RulePreviewRowViewModel> PreviewRows { get; } = [];

    public RelayCommand AddRuleCommand { get; }

    public RelayCommand PickScopeCommand { get; }

    public AsyncRelayCommand PreviewCommand { get; }

    public AsyncRelayCommand ApplyCommand { get; }

    public RuleConditionChoice SelectedCondition
    {
        get => _selectedCondition;
        set
        {
            if (value is not null && Set(ref _selectedCondition, value))
            {
                OnPropertyChanged(nameof(PatternHint));
                PatternMessage = null;
            }
        }
    }

    public string PatternHint => _selectedCondition.Hint;

    public string PatternText
    {
        get => _patternText;
        set
        {
            if (Set(ref _patternText, value ?? string.Empty))
            {
                PatternMessage = null;
            }
        }
    }

    public string? PatternMessage
    {
        get => _patternMessage;
        private set => Set(ref _patternMessage, value);
    }

    public RuleThemeChoice? SelectedTheme
    {
        get => _selectedTheme;
        set => Set(ref _selectedTheme, value);
    }

    /// <summary>The folder whose subfolders the rules look at.</summary>
    public string? ScopePath
    {
        get => _scopePath;
        private set
        {
            if (Set(ref _scopePath, value))
            {
                OnPropertyChanged(nameof(ScopeLabel));
                ClearPreview();
            }
        }
    }

    public string ScopeLabel => _scopePath ?? "No folder chosen yet.";

    /// <summary>How many levels of subfolders to look through.</summary>
    public int Depth
    {
        get => _depth;
        set
        {
            if (Set(ref _depth, Math.Clamp(value, RuleScanner.MinDepth, RuleScanner.MaxDepth)))
            {
                ClearPreview();
            }
        }
    }

    public string? PreviewSummary
    {
        get => _previewSummary;
        private set => Set(ref _previewSummary, value);
    }

    public bool HasPreview => PreviewRows.Count > 0;

    /// <summary>A problem with the saved rules.</summary>
    public string? Notice
    {
        get => _notice;
        private set => Set(ref _notice, value);
    }

    public void Load()
    {
        _rules.Clear();
        _rules.AddRange(_services.Rules.Load());
        Notice = _services.Rules.LoadProblem;
        ShowRules();
    }

    /// <summary>The themes changed, so the choices and each rule's wording may have too.</summary>
    public void ThemesChanged()
    {
        var chosen = SelectedTheme?.Id;

        Themes.Clear();

        foreach (var theme in _services.Themes.All)
        {
            Themes.Add(new RuleThemeChoice(theme.Id, theme.Name));
        }

        SelectedTheme = Themes.FirstOrDefault(theme => theme.Id == chosen) ?? Themes.FirstOrDefault();
        ShowRules();
    }

    private void AddRule()
    {
        var pattern = PatternText.Trim();

        if (!RuleMatcher.IsUsable(SelectedCondition.Condition, pattern))
        {
            PatternMessage = SelectedCondition.Condition == RuleCondition.MostlyFileType
                ? SelectedCondition.Hint
                : "Type what the rule should look for first.";
            return;
        }

        if (SelectedTheme is not { } theme)
        {
            PatternMessage = "Choose a look for this rule first.";
            return;
        }

        _rules.Add(new FolderRule
        {
            Id = Guid.NewGuid(),
            Condition = SelectedCondition.Condition,
            Pattern = pattern,
            ThemeId = theme.Id,
        });

        PatternText = string.Empty;
        SaveRules();
    }

    private void RemoveRule(FolderRule rule)
    {
        _rules.Remove(rule);
        SaveRules();
    }

    private void SaveRules()
    {
        try
        {
            _services.Rules.Save(_rules);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Log.Write("The rules could not be saved", ex);
            _dialogs.ShowError("Foldcraft couldn't save your rules. They'll last until you close the app.");
        }

        ShowRules();
        ClearPreview();
    }

    private void ShowRules()
    {
        Rules.Clear();

        foreach (var rule in _rules)
        {
            var name = _services.Themes.Find(rule.ThemeId)?.Name ?? "a theme that no longer exists";
            Rules.Add(new RuleRowViewModel(rule, PlainEnglish.RuleSummary(rule, name), new RelayCommand(() => RemoveRule(rule))));
        }

        RefreshCommands();
    }

    private void PickScope()
    {
        if (_dialogs.PickFolder("Choose a folder for the rules to look through") is { } folder)
        {
            ScopePath = folder;
            RefreshCommands();
        }
    }

    private void ClearPreview()
    {
        foreach (var row in PreviewRows)
        {
            row.PropertyChanged -= OnPreviewRowChanged;
        }

        PreviewRows.Clear();
        PreviewSummary = null;
        OnPropertyChanged(nameof(HasPreview));
        RefreshCommands();
    }

    private async Task PreviewAsync()
    {
        var cancellation = _activity.Begin("Looking through folders...", cancellable: true);
        var summary = "Something went wrong, and Foldcraft stopped looking.";

        ClearPreview();

        try
        {
            var scope = ScopePath!;
            var depth = Depth;
            var rules = _rules.ToArray();

            var preview = await Task.Run(
                () => _services.Scanner.Preview(scope, depth, rules, _services.Themes.Find, cancellation),
                CancellationToken.None);

            if (preview.ScopeRefusal is { } refusal)
            {
                summary = PlainEnglish.Refusal(refusal);
                PreviewSummary = summary;
                return;
            }

            // What will change goes first, because that's what this list is for.
            foreach (var row in preview.Rows.OrderBy(row => row.Status).ThenBy(row => row.FolderPath, StringComparer.OrdinalIgnoreCase))
            {
                var line = new RulePreviewRowViewModel(row);

                line.PropertyChanged += OnPreviewRowChanged;
                PreviewRows.Add(line);
            }

            summary = PlainEnglish.RulePreviewSummary(preview);
            PreviewSummary = summary;
            OnPropertyChanged(nameof(HasPreview));
        }
        finally
        {
            _activity.Finish(summary);
            RefreshCommands();
        }
    }

    private async Task ApplyAsync()
    {
        var chosen = PreviewRows.Where(row => row.IsIncluded).ToArray();

        if (chosen.Length == 0)
        {
            return;
        }

        var cancellation = _activity.Begin($"Applying to {PlainEnglish.Folders(chosen.Length)}...", chosen.Length, cancellable: true);
        var summary = "Something went wrong, and the remaining folders were left alone.";
        var finished = false;
        var progress = new Progress<FolderApplyResult>(_ =>
        {
            if (!finished)
            {
                _activity.Advance();
            }
        });

        try
        {
            var looks = chosen.Select(row => new FolderLook(row.Path, row.Row.Theme!.Spec)).ToArray();
            var batch = await Task.Run(() => _services.Customizer.ApplyEach(looks, cancellation, progress), CancellationToken.None);
            finished = true;

            foreach (var result in batch.Results.Where(r => r.Outcome is ApplyOutcome.Failed or ApplyOutcome.FailedRollbackIncomplete))
            {
                _services.Log.Write($"Rule apply {result.Outcome} for {result.RequestedPath}: {result.Message}");
            }

            summary = PlainEnglish.ApplySummary(batch.Results.ToArray());
        }
        finally
        {
            ClearPreview();
            _activity.Finish(summary);
            Applied?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPreviewRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        ApplyCommand.RaiseCanExecuteChanged();

    private void RefreshCommands()
    {
        PickScopeCommand.RaiseCanExecuteChanged();
        PreviewCommand.RaiseCanExecuteChanged();
        ApplyCommand.RaiseCanExecuteChanged();
    }
}
