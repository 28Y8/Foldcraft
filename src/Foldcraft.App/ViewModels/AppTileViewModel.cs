using Foldcraft.App.Mvvm;
using Foldcraft.App.Services;
using Foldcraft.Core.Apps;
using Foldcraft.Core.Icons;

namespace Foldcraft.App.ViewModels;

/// <summary>
/// One detected item: its live preview and the choices made for it. Every change redraws the
/// preview off the UI thread, and a newer change simply replaces an older one in progress.
/// </summary>
public sealed class AppTileViewModel : ObservableObject
{
    private const int TileSize = 96;
    private const int MediumSize = 32;
    private const int SmallSize = 16;

    private readonly IconRenderer _renderer;
    private readonly FileLog _log;
    private PreparedApp? _prepared;
    private AppLook? _look;
    private long _drawing;
    private bool _isIncluded;
    private string _folderName;
    private string _startColor = string.Empty;
    private string _endColor = string.Empty;
    private string? _colorMessage;
    private ShapeChoice _selectedShape;
    private LogoAnchorChoice _selectedAnchor;
    private LogoPlateChoice _selectedPlate;
    private double _scale = 1;
    private byte[]? _preview;
    private byte[]? _mediumPreview;
    private byte[]? _smallPreview;
    private string? _status;
    private StatusKind _statusKind;

    public AppTileViewModel(DetectedApp app, IconRenderer renderer, FileLog log)
    {
        App = app ?? throw new ArgumentNullException(nameof(app));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _folderName = app.Name;
        _selectedShape = Shapes[0];
        _selectedAnchor = Anchors[0];
        _selectedPlate = Plates[0];

        ResetCommand = new RelayCommand(Reset, () => IsReady);
    }

    /// <summary>Raised when the check box changes, so the screen can update its counts.</summary>
    public event EventHandler? IncludedChanged;

    public static IReadOnlyList<ShapeChoice> Shapes { get; } =
    [
        new("Windows 11 classic", FolderShape.Classic), new("Flat", FolderShape.Flat),
        new("Outline", FolderShape.Outline), new("Soft 3D", FolderShape.Soft3D),
    ];

    public static IReadOnlyList<LogoAnchorChoice> Anchors { get; } =
    [
        new("Center", LogoAnchor.Center), new("Bottom right", LogoAnchor.BottomRight), new("Bottom left", LogoAnchor.BottomLeft),
        new("Top right", LogoAnchor.TopRight), new("Top left", LogoAnchor.TopLeft),
    ];

    public static IReadOnlyList<LogoPlateChoice> Plates { get; } =
    [
        new("Automatic", null), new("None", LogoPlate.None), new("Light", LogoPlate.Light), new("Dark", LogoPlate.Dark),
    ];

    public double MinScale => LogoPlacement.MinScale;

    public double MaxScale => LogoPlacement.MaxScale;

    public DetectedApp App { get; }

    public string Name => App.Name;

    public string SourceName => PlainEnglish.AppSourceName(App.Source);

    public RelayCommand ResetCommand { get; }

    /// <summary>The last draw of the preview. Tests wait on this.</summary>
    public Task PreviewTask { get; private set; } = Task.CompletedTask;

    public bool IsReady => _look is not null;

    public bool HasLogo => _prepared?.Art is not null;

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (IsReady && Set(ref _isIncluded, value))
            {
                IncludedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string FolderName
    {
        get => _folderName;
        set => Set(ref _folderName, value ?? string.Empty);
    }

    /// <summary>The folder's color as #RRGGBB. Empty goes back to the color read from the logo.</summary>
    public string StartColor
    {
        get => _startColor;
        set
        {
            if (Set(ref _startColor, value?.Trim() ?? string.Empty))
            {
                Recompose();
            }
        }
    }

    /// <summary>The gradient's second color as #RRGGBB. Empty makes a solid folder.</summary>
    public string EndColor
    {
        get => _endColor;
        set
        {
            if (Set(ref _endColor, value?.Trim() ?? string.Empty))
            {
                Recompose();
            }
        }
    }

    public string? ColorMessage
    {
        get => _colorMessage;
        private set => Set(ref _colorMessage, value);
    }

    public ShapeChoice SelectedShape
    {
        get => _selectedShape;
        set
        {
            if (value is not null && Set(ref _selectedShape, value))
            {
                Recompose();
            }
        }
    }

    public LogoAnchorChoice SelectedAnchor
    {
        get => _selectedAnchor;
        set
        {
            if (value is not null && Set(ref _selectedAnchor, value))
            {
                Recompose();
            }
        }
    }

    public LogoPlateChoice SelectedPlate
    {
        get => _selectedPlate;
        set
        {
            if (value is not null && Set(ref _selectedPlate, value))
            {
                Recompose();
            }
        }
    }

    public double Scale
    {
        get => _scale;
        set
        {
            if (Set(ref _scale, Math.Clamp(Math.Round(value, 2), MinScale, MaxScale)))
            {
                Recompose();
            }
        }
    }

    public byte[]? Preview
    {
        get => _preview;
        private set => Set(ref _preview, value);
    }

    public byte[]? MediumPreview
    {
        get => _mediumPreview;
        private set => Set(ref _mediumPreview, value);
    }

    public byte[]? SmallPreview
    {
        get => _smallPreview;
        private set => Set(ref _smallPreview, value);
    }

    /// <summary>A note about this item's picture, or the result of the last run.</summary>
    public string? Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public StatusKind Kind
    {
        get => _statusKind;
        private set => Set(ref _statusKind, value);
    }

    public string QualityNote => _prepared switch
    {
        null => string.Empty,
        { Art: null } => "No icon could be read, so this uses a plain symbol.",
        { Art.Quality: ArtQuality.Low } => "This app's icon is small, so it's drawn smaller to stay sharp. Large folders may look bare.",
        _ => string.Empty,
    };

    public string ContrastNote => _look?.Fix switch
    {
        ContrastFix.ShiftedColor => "The logo was getting lost, so the folder's color was shifted.",
        ContrastFix.AddedPlate => "The logo was getting lost, so it sits on a plate.",
        _ => string.Empty,
    };

    /// <summary>Takes in the app's picture and colors, then draws the first preview.</summary>
    public Task AttachAsync(PreparedApp prepared)
    {
        _prepared = prepared ?? throw new ArgumentNullException(nameof(prepared));

        ForgetResult();
        OnPropertyChanged(nameof(QualityNote));
        OnPropertyChanged(nameof(HasLogo));
        Recompose();
        OnPropertyChanged(nameof(IsReady));
        ResetCommand.RaiseCanExecuteChanged();

        return PreviewTask;
    }

    /// <summary>Reading this one failed. It stays in the list so it isn't silently missing.</summary>
    public void ShowUnreadable()
    {
        Status = "This item's icon couldn't be read, so it can't be used.";
        Kind = StatusKind.Error;
    }

    /// <summary>The scan was stopped before this one's turn, so it has no preview and can't be checked yet.</summary>
    public void ShowNotRead()
    {
        Status = "Not read yet. Scan again to use this.";
        Kind = StatusKind.Warning;
    }

    public void ShowResult(string message, StatusKind kind)
    {
        Status = message;
        Kind = kind;
    }

    /// <summary>Goes back to saying only what's true of the picture. With no picture, what it says about that stands.</summary>
    public void ForgetResult()
    {
        if (_prepared is null)
        {
            return;
        }

        Status = string.IsNullOrEmpty(QualityNote) ? null : QualityNote;
        Kind = string.IsNullOrEmpty(QualityNote) ? StatusKind.Neutral : StatusKind.Warning;
    }

    /// <summary>What to make for this item, with its picture ready to save.</summary>
    /// <exception cref="InvalidOperationException">The item isn't ready.</exception>
    public AppFolderRequest ToRequest()
    {
        if (_prepared is null || _look is null)
        {
            throw new InvalidOperationException("This item isn't ready.");
        }

        return new AppFolderRequest(App, FolderName, _look.Spec, _prepared.Art?.Png);
    }

    private AppLookChoices Choices()
    {
        var start = TryColor(_startColor, out var startProblem);
        var end = TryColor(_endColor, out var endProblem);

        ColorMessage = startProblem || endProblem ? "Colors look like #2F6FED." : null;

        return new AppLookChoices
        {
            Start = start,
            End = start is null ? null : end,
            Shape = _selectedShape.Shape,
            Anchor = _selectedAnchor.Anchor,
            Plate = _selectedPlate.Plate,
            Scale = (float)_scale,
        };
    }

    private static IconColor? TryColor(string text, out bool isProblem)
    {
        isProblem = false;

        if (text.Length == 0)
        {
            return null;
        }

        try
        {
            return IconColor.Parse(text);
        }
        catch (FormatException)
        {
            isProblem = true;
            return null;
        }
    }

    private void Recompose()
    {
        if (_prepared is not { } prepared)
        {
            return;
        }

        _look = AppLookComposer.Compose(prepared, Choices());
        OnPropertyChanged(nameof(ContrastNote));
        PreviewTask = DrawAsync(_look.Spec, prepared.Art?.Png);
    }

    private async Task DrawAsync(FolderIconSpec spec, byte[]? art)
    {
        var version = ++_drawing;

        try
        {
            var frames = await Task.Run(() => new[] { TileSize, MediumSize, SmallSize }
                .Select(size => art is null ? _renderer.RenderPreviewPng(spec, size) : _renderer.RenderPreviewPng(spec, size, art))
                .ToArray());

            if (version == _drawing)
            {
                Preview = frames[0];
                MediumPreview = frames[1];
                SmallPreview = frames[2];
            }
        }
        catch (IconRenderException ex)
        {
            _log.Write($"The preview for {Name} could not be drawn", ex);
        }
    }

    private void Reset()
    {
        _startColor = string.Empty;
        _endColor = string.Empty;
        _selectedShape = Shapes[0];
        _selectedAnchor = Anchors[0];
        _selectedPlate = Plates[0];
        _scale = 1;

        foreach (var name in new[] { nameof(StartColor), nameof(EndColor), nameof(SelectedShape), nameof(SelectedAnchor), nameof(SelectedPlate), nameof(Scale) })
        {
            OnPropertyChanged(name);
        }

        Recompose();
    }
}
