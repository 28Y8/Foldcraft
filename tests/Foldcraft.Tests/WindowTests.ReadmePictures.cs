using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Foldcraft.App;
using Foldcraft.App.ViewModels;
using Foldcraft.Core.Icons;
using Foldcraft.Core.Rules;
using Bitmap = System.Drawing.Bitmap;
using Brushes = System.Drawing.Brushes;
using Color = System.Drawing.Color;
using Pen = System.Drawing.Pen;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Windows.Size;
using SolidBrush = System.Drawing.SolidBrush;

namespace Foldcraft.Tests;

/// <summary>
/// Setting FOLDCRAFT_README_DIR saves the pictures in docs\images. These are real renders of the
/// real window around the real view-models, on sample folders in a sandbox. The sandbox lives
/// under the temp folder, whose path holds the Windows account name, so right before each
/// picture every path on screen is swapped for a neutral sample one, and the picture is refused
/// if anything else is left. Each app's icon is drawn here, so no real product's art can appear.
/// </summary>
public sealed partial class WindowTests
{
    private const string SampleRoot = @"D:\Projects";
    private const string SampleDataFolder = @"C:\Users\You\AppData\Local\Foldcraft";

    /// <summary>Renders at twice the size, so the pictures stay sharp on a high-density screen.</summary>
    private const int PictureScale = 2;

    private static async Task TakeReadmePictures(App.App app)
    {
        var directory = Environment.GetEnvironmentVariable("FOLDCRAFT_README_DIR");

        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);

        using var harness = new AppHarness();

        var window = new MainWindow
        {
            DataContext = harness.Shell,
            Width = OpeningWidth,
            Height = OpeningHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            ShowActivated = false,
        };

        window.Show();
        await harness.Shell.InitializeAsync();
        app.ThemeMode = ThemeMode.Light;
        await Settle();

        await PictureHistory(window, harness, directory);
        await PictureCustomize(window, app, harness, directory);
        await PictureThemes(window, harness, directory);
        await PictureRules(window, harness, directory);
        await PictureApps(window, harness, directory);
        DrawFolderGallery(Path.Combine(directory, "folder-gallery.png"));

        window.Close();
    }

    private static async Task PictureHistory(Window window, AppHarness harness, string directory)
    {
        var photos = new[] { "Holiday Photos", "Family Photos", "Client Work", "Invoices" }.Select(harness.Box.NewFolder).ToArray();

        await harness.Customize.AddPathsAsync(photos[..2]);
        harness.Designer.SelectedSwatch = harness.Designer.Swatches[6];
        await harness.Designer.PreviewTask;
        await harness.Customize.ApplyCommand.ExecuteAsync();

        harness.Customize.ClearCommand.Execute(null);
        await harness.Customize.AddPathsAsync(photos[2..]);
        harness.Designer.SelectedSwatch = harness.Designer.Swatches[0];
        harness.Designer.SelectedGlyph = harness.Designer.Glyphs[0];
        harness.Designer.SelectedBadge = harness.Designer.Badges.First(b => b.Name == "Done");
        await harness.Designer.PreviewTask;
        await harness.Customize.ApplyCommand.ExecuteAsync();
        await harness.History.RefreshAsync();

        harness.Shell.SelectedTab = ShellViewModel.HistoryTab;
        await Settle();
        harness.History.Entries[0].IsExpanded = true;
        await Settle();

        Picture(window, harness, directory, "history");
    }

    private static async Task PictureCustomize(Window window, App.App app, AppHarness harness, string directory)
    {
        harness.Shell.SelectedTab = ShellViewModel.CustomizeTab;
        harness.Customize.ClearCommand.Execute(null);

        var folders = new[] { "Website", "Mobile App", "Design Assets", "Release Notes" }.Select(harness.Box.NewFolder).ToArray();

        await harness.Customize.AddPathsAsync(folders);

        harness.Designer.SelectedSwatch = harness.Designer.Swatches[9];
        harness.Designer.IsGradient = true;
        harness.Designer.EditingEnd = true;
        harness.Designer.SelectedSwatch = harness.Designer.Swatches[0];
        harness.Designer.SelectedGlyph = harness.Designer.Glyphs.First(g => g.GlyphId == "code");
        harness.Designer.SelectedBadge = harness.Designer.Badges.First(b => b.Name == "In Progress");
        await harness.Designer.PreviewTask;
        await Settle();

        Picture(window, harness, directory, "customize-light");

        app.ThemeMode = ThemeMode.Dark;
        await Settle();
        Picture(window, harness, directory, "customize-dark");

        app.ThemeMode = ThemeMode.Light;
        await Settle();
    }

    private static async Task PictureThemes(Window window, AppHarness harness, string directory)
    {
        harness.Shell.SelectedTab = ShellViewModel.ThemesTab;
        harness.Themes.SelectedTile = harness.Themes.Themes[9];
        await Settle();

        Picture(window, harness, directory, "themes");
    }

    private static async Task PictureRules(Window window, AppHarness harness, string directory)
    {
        var rules = harness.Rules;
        var scope = harness.Box.NewFolder("Sorted");

        foreach (var (name, extension) in new[] { ("Summer 2026", ".jpg"), ("Album Covers", ".png"), ("Band Practice", ".mp3"), ("Tax Papers", ".pdf") })
        {
            var folder = harness.Box.NewFolder(Path.Combine("Sorted", name));

            for (var i = 0; i < 3; i++)
            {
                File.WriteAllBytes(Path.Combine(folder, $"sample {i}{extension}"), []);
            }
        }

        rules.SelectedCondition = rules.Conditions.Single(c => c.Condition == RuleCondition.MostlyFileType);
        rules.PatternText = "pictures";
        rules.SelectedTheme = rules.Themes.First(t => t.Name == "Sunny");
        rules.AddRuleCommand.Execute(null);

        rules.PatternText = "music";
        rules.SelectedTheme = rules.Themes.First(t => t.Name == "Pop");
        rules.AddRuleCommand.Execute(null);

        rules.SelectedCondition = rules.Conditions.Single(c => c.Condition == RuleCondition.NameContains);
        rules.PatternText = "Tax";
        rules.SelectedTheme = rules.Themes.First(t => t.Name == "Sage");
        rules.AddRuleCommand.Execute(null);

        rules.SelectedCondition = rules.Conditions[0];
        rules.PatternText = string.Empty;

        harness.Dialogs.FolderToPick = scope;
        rules.PickScopeCommand.Execute(null);
        await rules.PreviewCommand.ExecuteAsync();

        harness.Shell.SelectedTab = ShellViewModel.RulesTab;
        await Settle();

        Picture(window, harness, directory, "rules");
    }

    private static async Task PictureApps(Window window, AppHarness harness, string directory)
    {
        var apps = harness.Shell.Apps;

        foreach (var (name, color, glyph) in new[]
                 {
                     ("Sample Paint", Color.FromArgb(0xE1, 0x1D, 0x48), "image"),
                     ("Sample Music", Color.FromArgb(0x7C, 0x3A, 0xED), "music"),
                     ("Sample Mail", Color.FromArgb(0x02, 0x84, 0xC7), "mail"),
                     ("Sample Notes", Color.FromArgb(0xF5, 0x9E, 0x0B), "pencil"),
                     ("Sample Code", Color.FromArgb(0x0F, 0x76, 0x6E), "code"),
                     ("Sample Racer", Color.FromArgb(0xEA, 0x58, 0x0C), "car"),
                     ("Sample Chess", Color.FromArgb(0x33, 0x41, 0x55), "star"),
                     ("Sample Quest", Color.FromArgb(0x16, 0xA3, 0x4A), "compass"),
                 })
        {
            AddDrawnApp(harness.Pc, name, color, glyph);
        }

        await apps.ScanCommand.ExecuteAsync();
        apps.SelectAllCommand.Execute(null);
        apps.SelectedTile = apps.Tiles[3];

        harness.Dialogs.FolderToPick = harness.Box.NewFolder("Games");
        apps.PickDestinationCommand.Execute(null);

        harness.Shell.SelectedTab = ShellViewModel.AppsTab;
        await Settle();

        Picture(window, harness, directory, "apps-and-games");
    }

    /// <summary>An app on the pretend PC, with an icon made here from one of Foldcraft's own symbols.</summary>
    private static void AddDrawnApp(FakePc pc, string name, Color color, string glyphId)
    {
        const int Size = 256;
        const float Scale = 7f;

        using var bitmap = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        using var plate = new GraphicsPath();
        using var fill = new SolidBrush(color);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        const int Corner = 96;

        plate.AddArc(8, 8, Corner, Corner, 180, 90);
        plate.AddArc(Size - 8 - Corner, 8, Corner, Corner, 270, 90);
        plate.AddArc(Size - 8 - Corner, Size - 8 - Corner, Corner, Corner, 0, 90);
        plate.AddArc(8, Size - 8 - Corner, Corner, Corner, 90, 90);
        plate.CloseFigure();
        graphics.FillPath(fill, plate);

        graphics.TranslateTransform((Size - (GlyphLayer.BoxSize * Scale)) / 2f, (Size - (GlyphLayer.BoxSize * Scale)) / 2f);
        graphics.ScaleTransform(Scale, Scale);

        foreach (var layer in GlyphCatalog.All.First(g => g.Id == glyphId).Layers)
        {
            using var shape = layer.ToPath();

            if (layer.IsStroke)
            {
                using var pen = new Pen(Color.White, layer.StrokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                graphics.DrawPath(pen, shape);
            }
            else
            {
                graphics.FillPath(Brushes.White, shape);
            }
        }

        var program = pc.AddProgram($@"{name}\{name}.exe");
        var icon = pc.WriteFile($@"Programs\{name}\{name}.ico", IcoFile.Write([bitmap]));

        pc.AddLink(pc.UserMenu, $"{name}.lnk", program, icon: icon);
    }

    /// <summary>The folder art by itself: every style, a spread of the built-in themes and each kind of mark.</summary>
    private static void DrawFolderGallery(string path)
    {
        const int Cell = 256;
        const int Gap = 16;

        string[] themes = ["sunny", "ember", "still-water", "midnight", "aurora", "signal"];

        var plain = FolderFill.Solid(IconColor.Parse("#2F6FED"));

        FolderIconSpec[] looks =
        [
            new() { Fill = plain, GlyphId = "star" },
            new() { Fill = plain, Shape = FolderShape.Flat, GlyphId = "music" },
            new() { Fill = plain, Shape = FolderShape.Outline, GlyphId = "code" },
            new() { Fill = plain, Shape = FolderShape.Soft3D, GlyphId = "heart" },
            new() { Fill = FolderFill.Solid(IconColor.Parse("#16A34A")), Badge = new IconBadge("done") },
            new() { Fill = FolderFill.Solid(IconColor.Parse("#F97316")), GlyphId = "image", Badge = new IconBadge("favorite") },
            .. themes.Select(id => Foldcraft.Core.Themes.BuiltInThemes.All.First(t => t.Id == "builtin:" + id).Spec),
        ];

        const int Columns = 6;

        var rows = looks.Length / Columns;
        var renderer = new IconRenderer();

        using var sheet = new Bitmap((Columns * (Cell + Gap)) + Gap, (rows * (Cell + Gap)) + Gap, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(sheet);

        for (var i = 0; i < looks.Length; i++)
        {
            using var stream = new MemoryStream(renderer.RenderPreviewPng(looks[i], Cell));
            using var icon = new Bitmap(stream);

            graphics.DrawImage(icon, new Rectangle(Gap + ((i % Columns) * (Cell + Gap)), Gap + ((i / Columns) * (Cell + Gap)), Cell, Cell));
        }

        sheet.Save(path, ImageFormat.Png);
    }

    private static void Picture(Window window, AppHarness harness, string directory, string name)
    {
        var box = harness.Box;

        // Longest first, so the data folder doesn't turn into a sample project.
        (string Real, string Sample)[] swaps =
        [
            (box.Paths.DataDirectory, SampleDataFolder),
            (Path.Combine(box.Root, "folders", "Sorted"), SampleRoot),
            (Path.Combine(box.Root, "folders"), SampleRoot),
            (box.Root, SampleRoot),
        ];

        foreach (var text in Find<TextBlock>(window).Where(t => t.Text.Contains(box.Root, StringComparison.OrdinalIgnoreCase)))
        {
            text.SetCurrentValue(TextBlock.TextProperty, Swap(text.Text, swaps));
        }

        foreach (var text in Find<TextBox>(window).Where(t => t.Text.Contains(box.Root, StringComparison.OrdinalIgnoreCase)))
        {
            text.SetCurrentValue(TextBox.TextProperty, Swap(text.Text, swaps));
        }

        window.UpdateLayout();

        var shown = Find<TextBlock>(window).Select(t => t.Text).Concat(Find<TextBox>(window).Select(t => t.Text));

        foreach (var text in shown)
        {
            Assert.False(
                text.Contains(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) ||
                text.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase),
                $"{name}: \"{text}\" would show a real path.");
        }

        Save(window, Path.Combine(directory, name + ".png"));
    }

    private static string Swap(string text, (string Real, string Sample)[] swaps)
    {
        foreach (var (real, sample) in swaps)
        {
            text = text.Replace(real, sample, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    private static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var size = new Size(content.ActualWidth, content.ActualHeight);
        var drawing = new DrawingVisual();

        using (var context = drawing.RenderOpen())
        {
            var background = new SolidColorBrush((System.Windows.Media.Color)Application.Current.FindResource("SolidBackgroundFillColorBase"));
            context.DrawRectangle(background, null, new Rect(size));

            var picture = new VisualBrush(content)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(size),
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };

            context.DrawRectangle(picture, null, new Rect(size));
        }

        var bitmap = new RenderTargetBitmap(
            (int)size.Width * PictureScale,
            (int)size.Height * PictureScale,
            96 * PictureScale,
            96 * PictureScale,
            PixelFormats.Pbgra32);

        bitmap.Render(drawing);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var file = File.Create(path);
        encoder.Save(file);
    }
}
