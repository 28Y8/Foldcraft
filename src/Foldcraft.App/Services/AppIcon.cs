using Foldcraft.Core.Icons;

namespace Foldcraft.App.Services;

/// <summary>
/// The app's own icon, which is simply one more Foldcraft folder. Assets\Foldcraft.ico is the
/// renderer's output for this spec. A test fails if the two drift apart, and setting
/// FOLDCRAFT_WRITE_APP_ICON=1 while running that test writes a fresh file.
/// </summary>
public static class AppIcon
{
    public static FolderIconSpec Spec { get; } = new()
    {
        Fill = FolderFill.Gradient(IconColor.Parse("#7C3AED"), IconColor.Parse("#2563EB"), 45f),
        GlyphId = "star",
    };
}
