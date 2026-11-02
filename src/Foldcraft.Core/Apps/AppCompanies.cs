namespace Foldcraft.Core.Apps;

/// <summary>
/// Adds an item for each well-known company with software on this PC, such as Adobe, for a folder
/// that stands for all of it. Only names live here. The icon is read from that company's own app
/// like any other, so no logo ships with Foldcraft and a company with nothing installed has no item.
/// </summary>
internal static class AppCompanies
{
    /// <summary>Each company, with the app whose icon stands for it best, if there's one.</summary>
    private static readonly (string Name, string? Lead)[] Known =
    [
        ("Adobe", "Adobe Creative Cloud"),
        ("Autodesk", null),
        ("Corel", null),
        ("Google", "Google Chrome"),
        ("JetBrains", "JetBrains Toolbox"),
        ("Microsoft", null),
    ];

    /// <summary>
    /// One item per company found. Its shortcut starts the lead app, because a folder needs
    /// something to hold, and it has no install folder, because a company doesn't have one.
    /// </summary>
    public static IEnumerable<DetectedApp> For(IReadOnlyList<DetectedApp> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);

        foreach (var (name, lead) in Known)
        {
            var products = apps
                .Where(app => !app.IsGame && app.Name.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase))
                .OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (products.Length == 0 || apps.Any(app => app.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var chosen = products.FirstOrDefault(app => app.Name.Equals(lead, StringComparison.OrdinalIgnoreCase)) ?? products[0];

            yield return chosen with { Id = $"company:{name}", Name = name, InstallFolder = null, ProgramPath = null };
        }
    }
}
