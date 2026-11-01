using System.Collections.ObjectModel;
using System.Globalization;
using Foldcraft.App.Mvvm;
using Foldcraft.App.Services;
using Foldcraft.Core.Icons;

namespace Foldcraft.App.ViewModels;

/// <summary>One color in the palette.</summary>
public sealed record SwatchViewModel(string Name, string Hex)
{
    public IconColor Color { get; } = IconColor.Parse(Hex);
}

/// <summary>One choice in the glyph picker. A null id means a plain folder.</summary>
public sealed class GlyphTileViewModel(string? glyphId, string name) : ObservableObject
{
    private byte[]? _image;

    public string? GlyphId { get; } = glyphId;
    public string Name { get; } = name;

    public byte[]? Image
    {
        get => _image;
        set => Set(ref _image, value);
    }
}

/// <summary>One choice in the badge picker. A null id means no badge.</summary>
public sealed class BadgeTileViewModel(string? badgeId, string name) : ObservableObject
{
    private byte[]? _image;

    public string? BadgeId { get; } = badgeId;
    public string Name { get; } = name;

    public byte[]? Image
    {
        get => _image;
        set => Set(ref _image, value);
    }
}

/// <summary>One line in the category filter. A null category means every symbol.</summary>
public sealed record GlyphCategoryChoice(string Name, GlyphCategory? Category);

/// <summary>One folder style.</summary>
public sealed record ShapeChoice(string Name, FolderShape Shape);

/// <summary>One corner for a badge.</summary>
public sealed record CornerChoice(string Name, BadgeCorner Corner);

/// <summary>
/// The look being designed, and its live preview. Every preview comes from the same Core
/// renderer that makes the real icon, off the UI thread. Rapid changes coalesce: only the
/// newest request is ever shown, however the renders happen to finish.
/// </summary>
public sealed class DesignerViewModel : ObservableObject
{
    public const int LargeSize = 256;
    public const int MediumSize = 32;
    public const int SmallSize = 16;

    private const int TileSize = 64;

    private readonly IconRenderer _renderer;
    private readonly IDialogs _dialogs;
    private readonly FileLog _log;
    private readonly TimeSpan _debounce;

    private IconColor _start;
    private IconColor _end;
    private bool _isGradient;
    private bool _endChosen;
    private bool _editingEnd;
    private double _angle = 90;
    private SwatchViewModel? _selectedSwatch;
    private GlyphTileViewModel? _selectedGlyph;
    private string? _imagePath;
    private string _hexText;
    private string? _hexMessage;
    private bool _isHexValid = true;
    private byte[]? _largePreview;
    private byte[]? _mediumPreview;
    private byte[]? _smallPreview;
    private string? _previewError;
    private long _previewVersion;
    private (FolderFill Fill, FolderShape Shape)? _tileLook;
    private double _displayScale = 1;
    private string _glyphSearch = string.Empty;
    private GlyphCategoryChoice _selectedCategory;
    private ShapeChoice _selectedShape;
    private BadgeTileViewModel _selectedBadge;
    private CornerChoice _selectedCorner;
    private string _badgeHexText = string.Empty;
    private IconColor? _badgeColor;
    private bool _isBadgeHexValid = true;
    private string _emojiText = string.Empty;
    private string? _emoji;
    private bool _isEmojiValid = true;

    public DesignerViewModel(IconRenderer renderer, IDialogs dialogs, FileLog log, TimeSpan? debounce = null)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _debounce = debounce ?? TimeSpan.FromMilliseconds(60);

        Swatches =
        [
            new("Blue", "#2F6FED"), new("Sky", "#0EA5E9"), new("Teal", "#0D9488"), new("Green", "#16A34A"),
            new("Lime", "#84CC16"), new("Yellow", "#FACC15"), new("Orange", "#F97316"), new("Red", "#DC2626"),
            new("Pink", "#EC4899"), new("Purple", "#9333EA"), new("Indigo", "#4F46E5"), new("Brown", "#92400E"),
            new("Tan", "#D6B370"), new("Slate", "#475569"), new("Graphite", "#1F2937"), new("Silver", "#CBD5E1"),
        ];

        Glyphs = [new GlyphTileViewModel(null, "Plain folder")];

        foreach (var glyph in GlyphCatalog.All)
        {
            Glyphs.Add(new GlyphTileViewModel(glyph.Id, glyph.DisplayName));
        }

        VisibleGlyphs = new ObservableCollection<GlyphTileViewModel>(Glyphs);

        GlyphCategories =
        [
            new GlyphCategoryChoice("All symbols", null),
            .. GlyphCatalog.Categories.Select(category => new GlyphCategoryChoice(category.ToString(), category)),
        ];

        Shapes =
        [
            new("Windows 11 classic", FolderShape.Classic), new("Flat", FolderShape.Flat),
            new("Outline", FolderShape.Outline), new("Soft 3D", FolderShape.Soft3D),
        ];

        Badges = [new BadgeTileViewModel(null, "No badge")];

        foreach (var badge in BadgeCatalog.All)
        {
            Badges.Add(new BadgeTileViewModel(badge.Id, badge.DisplayName));
        }

        Corners =
        [
            new("Bottom right", BadgeCorner.BottomRight), new("Bottom left", BadgeCorner.BottomLeft),
            new("Top right", BadgeCorner.TopRight), new("Top left", BadgeCorner.TopLeft),
        ];

        _selectedSwatch = Swatches[0];
        _selectedGlyph = Glyphs[0];
        _start = Swatches[0].Color;
        _end = Swatches[1].Color;
        _hexText = _start.ToHex();
        _selectedCategory = GlyphCategories[0];
        _selectedShape = Shapes[0];
        _selectedBadge = Badges[0];
        _selectedCorner = Corners[0];

        PickImageCommand = new RelayCommand(PickImage);
        ClearImageCommand = new RelayCommand(ClearImage, () => HasImage);
        PreviewTask = Task.CompletedTask;
    }

    /// <summary>Raised whenever <see cref="Spec"/> or <see cref="CanApply"/> changes.</summary>
    public event EventHandler? DesignChanged;

    public IReadOnlyList<SwatchViewModel> Swatches { get; }

    /// <summary>Every choice of symbol, in catalog order.</summary>
    public ObservableCollection<GlyphTileViewModel> Glyphs { get; }

    /// <summary>The symbols that fit the search and category.</summary>
    public ObservableCollection<GlyphTileViewModel> VisibleGlyphs { get; }

    public IReadOnlyList<GlyphCategoryChoice> GlyphCategories { get; }

    public IReadOnlyList<ShapeChoice> Shapes { get; }

    public ObservableCollection<BadgeTileViewModel> Badges { get; }

    public IReadOnlyList<CornerChoice> Corners { get; }

    public RelayCommand PickImageCommand { get; }

    public RelayCommand ClearImageCommand { get; }

    /// <summary>The newest preview render. Tests await this.</summary>
    public Task PreviewTask { get; private set; }

    public FolderIconSpec Spec => new()
    {
        Fill = IsGradient ? FolderFill.Gradient(_start, _end, (float)_angle) : FolderFill.Solid(_start),
        GlyphId = _imagePath is null && _emoji is null ? _selectedGlyph?.GlyphId : null,
        Emoji = _imagePath is null ? _emoji : null,
        CustomImagePath = _imagePath,
        Shape = _selectedShape.Shape,
        Badge = _selectedBadge.BadgeId is { } badge ? new IconBadge(badge, _selectedCorner.Corner, _badgeColor) : null,
    };

    /// <summary>False while the design on screen isn't something that can be applied.</summary>
    public bool CanApply => _isHexValid && _isBadgeHexValid && _isEmojiValid && PreviewError is null;

    /// <summary>Words to narrow the symbols down. Names and related words both count.</summary>
    public string GlyphSearch
    {
        get => _glyphSearch;
        set
        {
            if (Set(ref _glyphSearch, value ?? string.Empty))
            {
                FilterGlyphs();
            }
        }
    }

    public GlyphCategoryChoice SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (value is not null && Set(ref _selectedCategory, value))
            {
                FilterGlyphs();
            }
        }
    }

    /// <summary>True when the search and category leave nothing to show.</summary>
    public bool NoGlyphsFound => VisibleGlyphs.Count == 0;

    public ShapeChoice SelectedShape
    {
        get => _selectedShape;
        set
        {
            if (value is not null && Set(ref _selectedShape, value))
            {
                DesignUpdated();
            }
        }
    }

    /// <summary>One emoji to use instead of a symbol. Clearing the box goes back to the plain folder.</summary>
    public string EmojiText
    {
        get => _emojiText;
        set
        {
            if (!Set(ref _emojiText, value ?? string.Empty))
            {
                return;
            }

            var text = _emojiText.Trim();

            if (text.Length == 0)
            {
                ApplyEmoji(null, valid: true);
            }
            else if (Core.Icons.EmojiText.IsValid(text))
            {
                ApplyEmoji(text, valid: true);
            }
            else
            {
                ApplyEmoji(_emoji, valid: false);
            }
        }
    }

    public string? EmojiMessage => _isEmojiValid ? null : "That isn't one emoji. Try pasting a single emoji, or press Win + . to pick one.";

    public BadgeTileViewModel SelectedBadge
    {
        get => _selectedBadge;
        set
        {
            if (value is not null && Set(ref _selectedBadge, value))
            {
                OnPropertyChanged(nameof(HasBadge));
                DesignUpdated();
            }
        }
    }

    public bool HasBadge => _selectedBadge.BadgeId is not null;

    public CornerChoice SelectedCorner
    {
        get => _selectedCorner;
        set
        {
            if (value is not null && Set(ref _selectedCorner, value))
            {
                DesignUpdated();
            }
        }
    }

    /// <summary>The badge's color. Left empty, each badge uses its own.</summary>
    public string BadgeHexText
    {
        get => _badgeHexText;
        set
        {
            if (!Set(ref _badgeHexText, value ?? string.Empty))
            {
                return;
            }

            var text = _badgeHexText.Trim();
            var digits = text.StartsWith('#') ? text[1..] : text;

            if (digits.Length == 0)
            {
                SetBadgeColor(null, valid: true);
            }
            else if (digits.Length == 6 && digits.All(char.IsAsciiHexDigit))
            {
                SetBadgeColor(IconColor.Parse(digits), valid: true);
            }
            else
            {
                SetBadgeColor(_badgeColor, valid: false);
            }
        }
    }

    public string? BadgeHexMessage => _isBadgeHexValid ? null : "Use six digits from 0 to 9 and A to F, like #16A34A, or leave this empty.";

    public SwatchViewModel? SelectedSwatch
    {
        get => _selectedSwatch;
        set
        {
            if (Set(ref _selectedSwatch, value) && value is not null)
            {
                SetActiveColor(value.Color);
            }
        }
    }

    /// <summary>What's in the color box. Text that isn't a color yet changes nothing.</summary>
    public string HexText
    {
        get => _hexText;
        set
        {
            if (!Set(ref _hexText, value))
            {
                return;
            }

            var text = (value ?? string.Empty).Trim();
            var digits = text.StartsWith('#') ? text[1..] : text;

            if (digits.Length == 6 && digits.All(char.IsAsciiHexDigit))
            {
                HexMessage = null;
                SetHexValid(true);
                SetActiveColor(IconColor.Parse(digits), keepHexText: true);
                return;
            }

            HexMessage = digits.Length < 6 && digits.All(char.IsAsciiHexDigit)
                ? "Keep typing: a color has six digits, like #2F6FED."
                : "That isn't a color. Use six digits from 0 to 9 and A to F, like #2F6FED.";

            SetHexValid(false);
        }
    }

    public string? HexMessage
    {
        get => _hexMessage;
        private set => Set(ref _hexMessage, value);
    }

    /// <summary>The color being edited, for the small sample beside the box.</summary>
    public string ActiveHex => (EditingEnd ? _end : _start).ToHex();

    public bool IsGradient
    {
        get => _isGradient;
        set
        {
            if (!Set(ref _isGradient, value))
            {
                return;
            }

            if (value && !_endChosen)
            {
                _end = SwatchAfter(_start);
            }

            if (!value)
            {
                EditingEnd = false;
            }

            DesignUpdated();
        }
    }

    public bool EditingEnd
    {
        get => _editingEnd;
        set
        {
            if (Set(ref _editingEnd, value))
            {
                OnPropertyChanged(nameof(EditingStart));
                ShowActiveColor();
            }
        }
    }

    public bool EditingStart
    {
        get => !_editingEnd;
        set => EditingEnd = !value;
    }

    /// <summary>Degrees clockwise from left-to-right, so 90 runs from the top down.</summary>
    public double Angle
    {
        get => _angle;
        set
        {
            if (Set(ref _angle, value))
            {
                OnPropertyChanged(nameof(AngleText));
                DesignUpdated();
            }
        }
    }

    public string AngleText => string.Create(CultureInfo.CurrentCulture, $"{_angle:0}°");

    public GlyphTileViewModel? SelectedGlyph
    {
        get => _selectedGlyph;
        set
        {
            // The list clears its selection when a picture replaces the glyph, or a search hides
            // it. That isn't a choice. A plain tile picked again after an emoji still is one.
            if (value is null || (!Set(ref _selectedGlyph, value) && _emoji is null))
            {
                return;
            }

            SetImage(null);
            ClearEmoji();
            DesignUpdated();
        }
    }

    public bool HasImage => _imagePath is not null;

    public string ImageName => _imagePath is null ? string.Empty : Path.GetFileName(_imagePath);

    /// <summary>
    /// The display's scaling, where 1.5 means 150%. Explorer's 16 px icon covers 24 screen
    /// pixels at that setting, so the small previews are drawn at the same real size.
    /// </summary>
    public double DisplayScale
    {
        get => _displayScale;
        set
        {
            if (value > 0 && Set(ref _displayScale, value))
            {
                RequestPreview();
            }
        }
    }

    public byte[]? LargePreview
    {
        get => _largePreview;
        private set => Set(ref _largePreview, value);
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

    public string? PreviewError
    {
        get => _previewError;
        private set
        {
            if (Set(ref _previewError, value))
            {
                OnPropertyChanged(nameof(CanApply));
                DesignChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Draws the first preview.</summary>
    public Task InitializeAsync()
    {
        RequestPreview();
        return PreviewTask;
    }

    /// <summary>Shows a saved look in the designer, so it can be applied or adjusted.</summary>
    public void LoadLook(FolderIconSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        _start = spec.Fill.Start;
        _isGradient = spec.Fill.End is not null;
        _end = spec.Fill.End ?? SwatchAfter(_start);
        _endChosen = spec.Fill.End is not null;
        _angle = spec.Fill.AngleDegrees;
        _editingEnd = false;

        _selectedShape = Shapes.First(choice => choice.Shape == spec.Shape);
        _selectedGlyph = spec.GlyphId is null
            ? (spec.Emoji is null && spec.CustomImagePath is null ? Glyphs[0] : null)
            : Glyphs.FirstOrDefault(tile => tile.GlyphId == spec.GlyphId) ?? Glyphs[0];

        _emoji = spec.Emoji;
        _emojiText = spec.Emoji ?? string.Empty;
        _isEmojiValid = true;

        _selectedBadge = Badges.FirstOrDefault(tile => tile.BadgeId == spec.Badge?.Kind) ?? Badges[0];
        _selectedCorner = Corners.First(choice => choice.Corner == (spec.Badge?.Corner ?? BadgeCorner.BottomRight));
        _badgeColor = spec.Badge?.Color;
        _badgeHexText = spec.Badge?.Color?.ToHex() ?? string.Empty;
        _isBadgeHexValid = true;

        foreach (var name in new[]
                 {
                     nameof(IsGradient), nameof(EditingEnd), nameof(EditingStart), nameof(Angle), nameof(AngleText),
                     nameof(SelectedShape), nameof(SelectedGlyph), nameof(EmojiText), nameof(EmojiMessage),
                     nameof(SelectedBadge), nameof(HasBadge), nameof(SelectedCorner), nameof(BadgeHexText),
                     nameof(BadgeHexMessage), nameof(CanApply),
                 })
        {
            OnPropertyChanged(name);
        }

        SetImage(spec.CustomImagePath);
        ShowActiveColor();
        DesignUpdated();
    }

    private void ApplyEmoji(string? emoji, bool valid)
    {
        var changed = _emoji != emoji;

        _emoji = emoji;

        if (_isEmojiValid != valid)
        {
            _isEmojiValid = valid;
            OnPropertyChanged(nameof(EmojiMessage));
            OnPropertyChanged(nameof(CanApply));
        }

        if (changed && emoji is not null)
        {
            SetImage(null);
            Set(ref _selectedGlyph, null, nameof(SelectedGlyph));
        }
        else if (changed)
        {
            Set(ref _selectedGlyph, Glyphs[0], nameof(SelectedGlyph));
        }

        DesignUpdated();
    }

    private void ClearEmoji()
    {
        _emoji = null;
        _isEmojiValid = true;
        Set(ref _emojiText, string.Empty, nameof(EmojiText));
        OnPropertyChanged(nameof(EmojiMessage));
        OnPropertyChanged(nameof(CanApply));
    }

    private void SetBadgeColor(IconColor? color, bool valid)
    {
        _badgeColor = color;

        if (_isBadgeHexValid != valid)
        {
            _isBadgeHexValid = valid;
            OnPropertyChanged(nameof(BadgeHexMessage));
            OnPropertyChanged(nameof(CanApply));
        }

        DesignUpdated();
    }

    private void FilterGlyphs()
    {
        var found = GlyphCatalog.Search(_glyphSearch, _selectedCategory.Category).Select(glyph => glyph.Id).ToHashSet();
        var searching = _glyphSearch.Trim().Length > 0 || _selectedCategory.Category is not null;

        VisibleGlyphs.Clear();

        foreach (var tile in Glyphs.Where(tile => tile.GlyphId is null ? !searching : found.Contains(tile.GlyphId)))
        {
            VisibleGlyphs.Add(tile);
        }

        OnPropertyChanged(nameof(NoGlyphsFound));

        // Emptying the list dropped its selection, so it needs telling again.
        OnPropertyChanged(nameof(SelectedGlyph));
    }

    private void PickImage()
    {
        if (_dialogs.PickImage() is not { } path)
        {
            return;
        }

        SetImage(path);
        ClearEmoji();
        Set(ref _selectedGlyph, null, nameof(SelectedGlyph));
        DesignUpdated();
    }

    private void ClearImage()
    {
        SetImage(null);
        Set(ref _selectedGlyph, Glyphs[0], nameof(SelectedGlyph));
        DesignUpdated();
    }

    private void SetImage(string? path)
    {
        _imagePath = path;
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(ImageName));
        ClearImageCommand.RaiseCanExecuteChanged();
    }

    private void SetHexValid(bool valid)
    {
        if (_isHexValid != valid)
        {
            _isHexValid = valid;
            OnPropertyChanged(nameof(CanApply));
            DesignChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetActiveColor(IconColor color, bool keepHexText = false)
    {
        if (EditingEnd)
        {
            _end = color;
            _endChosen = true;
        }
        else
        {
            _start = color;
        }

        ShowActiveColor(keepHexText);
        DesignUpdated();
    }

    /// <summary>Makes the palette and the color box agree with the color being edited.</summary>
    private void ShowActiveColor(bool keepHexText = false)
    {
        var color = EditingEnd ? _end : _start;

        Set(ref _selectedSwatch, Swatches.FirstOrDefault(s => s.Color == color), nameof(SelectedSwatch));
        OnPropertyChanged(nameof(ActiveHex));

        if (!keepHexText)
        {
            Set(ref _hexText, color.ToHex(), nameof(HexText));
            HexMessage = null;
            SetHexValid(true);
        }
    }

    private IconColor SwatchAfter(IconColor color)
    {
        var index = Swatches.ToList().FindIndex(s => s.Color == color);
        return Swatches[(index + 1) % Swatches.Count].Color;
    }

    private void DesignUpdated()
    {
        DesignChanged?.Invoke(this, EventArgs.Empty);
        RequestPreview();
    }

    private void RequestPreview()
    {
        var version = Interlocked.Increment(ref _previewVersion);
        PreviewTask = RenderPreviewAsync(version, Spec, _displayScale);
    }

    private static int AtScale(int size, double scale) => Math.Clamp((int)Math.Round(size * scale), SmallSize, LargeSize);

    private bool IsCurrent(long version) => Interlocked.Read(ref _previewVersion) == version;

    private async Task RenderPreviewAsync(long version, FolderIconSpec spec, double scale)
    {
        try
        {
            if (_debounce > TimeSpan.Zero)
            {
                await Task.Delay(_debounce);

                if (!IsCurrent(version))
                {
                    return;
                }
            }

            var (large, medium, small) = await Task.Run(() => (
                _renderer.RenderPreviewPng(spec, LargeSize),
                _renderer.RenderPreviewPng(spec, AtScale(MediumSize, scale)),
                _renderer.RenderPreviewPng(spec, AtScale(SmallSize, scale))));

            if (!IsCurrent(version))
            {
                return;
            }

            LargePreview = large;
            MediumPreview = medium;
            SmallPreview = small;
            PreviewError = null;

            await RenderTilesAsync(version, spec.Fill, spec.Shape);
        }
        catch (IconRenderException ex)
        {
            if (IsCurrent(version))
            {
                PreviewError = ex.Message;
            }
        }
        catch (Exception ex)
        {
            _log.Write("The preview could not be drawn", ex);

            if (IsCurrent(version))
            {
                PreviewError = "The preview couldn't be drawn.";
            }
        }
    }

    /// <summary>Redraws the glyph picker in the chosen colors, only when they changed.</summary>
    private async Task RenderTilesAsync(long version, FolderFill fill, FolderShape shape)
    {
        if (_tileLook == (fill, shape))
        {
            return;
        }

        var glyphs = Glyphs.Select(tile => tile.GlyphId).ToArray();
        var badges = Badges.Select(tile => tile.BadgeId).ToArray();

        var (glyphImages, badgeImages) = await Task.Run(() => (
            glyphs.Select(id => _renderer.RenderPreviewPng(new FolderIconSpec { Fill = fill, Shape = shape, GlyphId = id }, TileSize)).ToArray(),
            badges.Select(id => _renderer.RenderPreviewPng(
                new FolderIconSpec { Fill = fill, Shape = shape, Badge = id is null ? null : new IconBadge(id) }, TileSize)).ToArray()));

        if (!IsCurrent(version))
        {
            return;
        }

        _tileLook = (fill, shape);

        for (var i = 0; i < glyphImages.Length; i++)
        {
            Glyphs[i].Image = glyphImages[i];
        }

        for (var i = 0; i < badgeImages.Length; i++)
        {
            Badges[i].Image = badgeImages[i];
        }
    }
}
