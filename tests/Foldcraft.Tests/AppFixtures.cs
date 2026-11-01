using System.Drawing;
using System.Drawing.Imaging;
using Foldcraft.Core.Apps;
using Foldcraft.Core.Icons;
using Foldcraft.Core.Shell;

namespace Foldcraft.Tests;

/// <summary>Pictures drawn here, in code. No test uses art from any real product.</summary>
internal static class DrawnArt
{
    public static byte[] Png(int size, Color color, int margin = 0)
    {
        using var bitmap = Bitmap(size, color, margin);
        return Encode(bitmap);
    }

    public static Bitmap Bitmap(int size, Color color, int margin = 0)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(bitmap);
        using var brush = new SolidBrush(color);

        graphics.FillRectangle(brush, margin, margin, size - (margin * 2), size - (margin * 2));
        return bitmap;
    }

    /// <summary>A logo split between two colors, with the given share in the first.</summary>
    public static byte[] TwoColorPng(int size, Color first, Color second, float firstShare)
    {
        using var bitmap = Bitmap(size, second);
        using var graphics = Graphics.FromImage(bitmap);
        using var brush = new SolidBrush(first);

        graphics.FillRectangle(brush, 0, 0, size * firstShare, size);
        return Encode(bitmap);
    }

    public static byte[] Encode(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>A real .ico with a frame at each size. 256 is PNG and the rest are the older kind.</summary>
    public static byte[] Ico(Color color, params int[] sizes)
    {
        var frames = sizes.Select(size => Bitmap(size, color)).ToArray();

        try
        {
            return IcoFile.Write(frames);
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    public static Color PixelAt(byte[] png, int x, int y)
    {
        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);
        return bitmap.GetPixel(x, y);
    }

    public static (int Width, int Height) SizeOf(byte[] png)
    {
        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);
        return (bitmap.Width, bitmap.Height);
    }

    /// <summary>How many pixels differ noticeably between two pictures of the same size.</summary>
    public static int Difference(byte[] first, byte[] second)
    {
        using var a = new Bitmap(new MemoryStream(first));
        using var b = new Bitmap(new MemoryStream(second));
        var count = 0;

        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var (p, q) = (a.GetPixel(x, y), b.GetPixel(x, y));

                if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) + Math.Abs(p.A - q.A) > 40)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>How many pixels are close to a color.</summary>
    public static int PixelsLike(byte[] png, Color color)
    {
        using var bitmap = new Bitmap(new MemoryStream(png));
        var count = 0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);

                if (pixel.A > 200 && Math.Abs(pixel.R - color.R) + Math.Abs(pixel.G - color.G) + Math.Abs(pixel.B - color.B) < 60)
                {
                    count++;
                }
            }
        }

        return count;
    }
}

/// <summary>
/// A pretend PC inside a sandbox: launcher files, Start Menus and installs laid out the way the
/// real ones are, with made-up names. A scan pointed here can't see the real machine.
/// </summary>
internal sealed class FakePc
{
    private readonly ShellShortcuts _shortcuts = new();
    private readonly string _scratch;

    public FakePc(string root, IUserRegistry? registry)
    {
        Root = root;
        _scratch = Path.Combine(root, "scratch");
        UserMenu = Path.Combine(root, "UserMenu");
        AllUsersMenu = Path.Combine(root, "AllUsersMenu");
        LocalAppData = Path.Combine(root, "Local");
        ProgramData = Path.Combine(root, "ProgramData");
        Windows = Path.Combine(root, "Windows");
        Steam = Path.Combine(root, "Launchers", "Steam");

        Directory.CreateDirectory(UserMenu);
        Directory.CreateDirectory(AllUsersMenu);
        Directory.CreateDirectory(LocalAppData);
        Directory.CreateDirectory(ProgramData);
        Directory.CreateDirectory(Windows);

        Roots = new AppDiscoveryRoots
        {
            Registry = registry,
            StartMenuFolders = [UserMenu, AllUsersMenu],
            LocalAppData = LocalAppData,
            ProgramData = ProgramData,
            WindowsDirectory = Windows,
        };
    }

    public string Root { get; }
    public string UserMenu { get; }
    public string AllUsersMenu { get; }
    public string LocalAppData { get; }
    public string ProgramData { get; }
    public string Windows { get; }
    public string Steam { get; }
    public AppDiscoveryRoots Roots { get; }

    public AppDiscovery Discovery => AppDiscovery.For(Roots, _shortcuts);

    /// <summary>Installs the launcher, with the registry value spelled the way Steam writes it.</summary>
    public void InstallSteam(FakeRegistry registry, string? libraryFile = null)
    {
        Directory.CreateDirectory(Path.Combine(Steam, "steamapps"));
        File.WriteAllBytes(Path.Combine(Steam, "steam.exe"), []);
        registry.SetString(@"Software\Valve\Steam", "SteamPath", Steam.Replace('\\', '/').ToLowerInvariant());

        if (libraryFile is not null)
        {
            File.WriteAllText(Path.Combine(Steam, "steamapps", "libraryfolders.vdf"), libraryFile);
        }
    }

    /// <summary>Adds a game's manifest, and its install folder unless the name is null.</summary>
    public void AddSteamGame(string library, string appId, string name, string? installDir = "Game Folder")
    {
        var apps = Path.Combine(library, "steamapps");
        Directory.CreateDirectory(apps);

        var manifest = $"\"AppState\"\n{{\n\t\"appid\"\t\t\"{appId}\"\n\t\"name\"\t\t\"{name}\"\n\t\"StateFlags\"\t\t\"4\"\n";

        if (installDir is not null)
        {
            manifest += $"\t\"installdir\"\t\t\"{installDir}\"\n";
            Directory.CreateDirectory(Path.Combine(apps, "common", installDir));
        }

        File.WriteAllText(Path.Combine(apps, $"appmanifest_{appId}.acf"), manifest + "}\n");
    }

    /// <summary>Makes an empty file to stand in for a program, which is all discovery needs.</summary>
    public string AddProgram(string relativePath)
    {
        var path = Path.Combine(Root, "Programs", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }

    /// <summary>Writes a real shortcut, the way an installer would.</summary>
    public string AddLink(string menu, string relativePath, string target, string? arguments = null, string? icon = null, int iconIndex = 0)
    {
        var path = Path.Combine(menu, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, _shortcuts.Build(new ShortcutInfo(target, arguments, null, icon, iconIndex), _scratch));
        return path;
    }

    public string WriteFile(string relativePath, byte[] bytes)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Adds an app to the Start Menu with an icon drawn in the given color.</summary>
    public string AddApp(string name, Color color, int iconSize = 256)
    {
        var program = AddProgram($@"{name}\{name}.exe");
        var icon = WriteFile($@"Programs\{name}\{name}.ico", DrawnArt.Ico(color, iconSize));
        AddLink(UserMenu, $"{name}.lnk", program, icon: icon);
        return program;
    }

    public void AddEpicManifest(string fileName, string json)
    {
        var folder = Path.Combine(ProgramData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, fileName), json);
    }

    /// <summary>Adds an install in a version folder, which is how Roblox lays itself out.</summary>
    public string AddRobloxVersion(string version, string program)
    {
        var path = Path.Combine(LocalAppData, "Roblox", "Versions", version, program);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }
}

/// <summary>A provider that behaves however a test needs.</summary>
internal sealed class ScriptedProvider(Func<CancellationToken, AppProviderResult> discover) : IAppProvider
{
    public string Name => "Scripted";

    public AppProviderResult Discover(CancellationToken cancellationToken) => discover(cancellationToken);
}

/// <summary>Stands in for icon reading, to get a failure or a pause at exactly the right moment.</summary>
internal sealed class ScriptedIconReader(Func<IReadOnlyList<IconCandidate>, CancellationToken, AppArt?> read) : IAppIconReader
{
    public AppArt? Read(IReadOnlyList<IconCandidate> candidates, CancellationToken cancellationToken = default) =>
        read(candidates, cancellationToken);
}

internal static class SampleApps
{
    public static DetectedApp Program(string name = "Sample App", string target = @"C:\Apps\Sample\sample.exe") => new()
    {
        Id = $"test:{name}",
        Name = name,
        Source = AppSource.StartMenu,
        Launch = new AppLaunch(LaunchKind.Program, target, "--start", @"C:\Apps\Sample"),
    };

    public static DetectedApp SteamGame(string name = "Sample Game", string appId = "4242", string? installFolder = null) => new()
    {
        Id = $"steam:{appId}",
        Name = name,
        Source = AppSource.Steam,
        Launch = new AppLaunch(LaunchKind.Address, $"steam://rungameid/{appId}"),
        InstallFolder = installFolder,
        IsGame = true,
    };

    /// <summary>A look with a picture, ready to hand to the creator.</summary>
    public static AppFolderRequest Request(DetectedApp app, string? folderName = null)
    {
        var art = DrawnArt.Png(128, Color.FromArgb(200, 40, 60));

        var spec = new FolderIconSpec
        {
            Fill = FolderFill.Gradient(IconColor.Parse("#C83C50"), IconColor.Parse("#7A1E3C"), 115f),
            CustomImagePath = AppLookComposer.ArtNotSavedYet,
            Logo = new LogoPlacement(),
        };

        return new AppFolderRequest(app, folderName ?? app.Name, spec, art);
    }
}
