namespace Foldcraft.Tests;

/// <summary>Trait values, so a test run can leave out what the machine can't do.</summary>
internal static class TestCategories
{
    /// <summary>
    /// Opens a real window, so it needs a desktop session and a screen as large as the app's
    /// window. Run everything else with <c>dotnet test --filter "Category!=Desktop"</c>.
    /// </summary>
    public const string Desktop = "Desktop";
}
