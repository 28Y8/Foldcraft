using System.Text.RegularExpressions;

namespace Foldcraft.Tests;

/// <summary>What the owner's writing and licensing rules ask of every file in the project.</summary>
public sealed partial class WritingRulesTests
{
    /// <summary>Written as a number, so this file passes its own test.</summary>
    private const char EmDash = (char)0x2014;

    private static readonly string[] Extensions =
    [
        ".cs", ".xaml", ".md", ".csproj", ".props", ".manifest", ".yml", ".ps1", ".editorconfig", ".gitattributes", ".gitignore",
    ];

    private static readonly string[] BritishSpellings =
    [
        "colour", "centre", "licence", "cancelled", "cancelling", "behaviour", "favourite", "honour", "grey", "organis", "customis", "recognis", "catalogue",
    ];

    /// <summary>What doesn't count as the project: build output, test output and the local test build.</summary>
    private static readonly string[] OutputFolders = [@"\bin\", @"\obj\", @"\.git\", @"\artifacts\", @"\TestResults\"];

    /// <summary>
    /// Every picture in the repository, by name. Each one is a render of Foldcraft itself, made
    /// by the window test. Adding to this list means checking the new picture holds no
    /// third-party logo and no personal data.
    /// </summary>
    private static readonly string[] OurPictures =
    [
        "apps-and-games.png", "customize-dark.png", "customize-light.png", "folder-gallery.png", "history.png", "rules.png", "themes.png",
    ];

    private static readonly string[] PictureExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp"];

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Foldcraft.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository wasn't found.");
    }

    private static bool IsOutput(string file) =>
        OutputFolders.Any(folder => file.Contains(folder, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> ProjectFiles() =>
        Directory.EnumerateFiles(RepositoryRoot(), "*", SearchOption.AllDirectories)
            .Where(file => Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Where(file => !IsOutput(file));

    [Fact]
    public void No_file_uses_an_em_dash()
    {
        Assert.All(ProjectFiles(), file => Assert.DoesNotContain(EmDash, File.ReadAllText(file)));
    }

    [Fact]
    public void Spelling_is_american_throughout()
    {
        foreach (var file in ProjectFiles().Where(file => Path.GetFileName(file) != nameof(WritingRulesTests) + ".cs"))
        {
            var text = File.ReadAllText(file);

            foreach (var word in BritishSpellings)
            {
                Assert.False(text.Contains(word, StringComparison.OrdinalIgnoreCase), $"{file} uses \"{word}\".");
            }
        }
    }

    /// <summary>This only sees lists of single words, because a comma can rightly join two whole clauses.</summary>
    [Fact]
    public void No_list_has_a_comma_before_its_last_item()
    {
        foreach (var file in ProjectFiles())
        {
            var match = CommaBeforeTheLastItem().Match(File.ReadAllText(file));

            Assert.False(match.Success, $"{file} has \"{match.Value}\".");
        }
    }

    [Fact]
    public void The_only_pictures_are_our_own_renders_in_the_docs()
    {
        var root = RepositoryRoot();
        var images = Path.Combine(root, "docs", "images");

        var found = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => PictureExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Where(file => !IsOutput(file))
            .ToArray();

        Assert.All(found, file => Assert.Equal(images, Path.GetDirectoryName(file), StringComparer.OrdinalIgnoreCase));
        Assert.Equal(OurPictures.Order(StringComparer.Ordinal), found.Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal));
    }

    [GeneratedRegex(@"\b[a-z]+, [a-z]+, (and|or) [a-z]+\b")]
    private static partial Regex CommaBeforeTheLastItem();
}
