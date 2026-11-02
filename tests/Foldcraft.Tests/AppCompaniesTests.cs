using Foldcraft.Core.Apps;

namespace Foldcraft.Tests;

public sealed class AppCompaniesTests
{
    private sealed class ListedApps(params DetectedApp[] apps) : IAppProvider
    {
        public string Name => "Listed";

        public AppProviderResult Discover(CancellationToken cancellationToken) => new(apps);
    }

    private static DetectedApp App(string name, bool isGame = false) => new()
    {
        Id = $"test:{name}",
        Name = name,
        Source = AppSource.StartMenu,
        Launch = new AppLaunch(LaunchKind.Program, $@"D:\Apps\{name}.exe"),
        Icons = [new IconCandidate($@"D:\Apps\{name}.exe")],
        InstallFolder = $@"D:\Apps\{name}",
        ProgramPath = $@"D:\Apps\{name}.exe",
        IsGame = isGame,
    };

    private static IReadOnlyList<DetectedApp> Scan(params DetectedApp[] apps) =>
        new AppDiscovery([new ListedApps(apps)]).Scan().Apps;

    [Fact]
    public void A_company_gets_an_item_of_its_own_beside_its_apps()
    {
        var apps = Scan(App("Adobe Photoshop 2026"), App("Adobe Illustrator 2026"));

        Assert.Equal(["Adobe", "Adobe Illustrator 2026", "Adobe Photoshop 2026"], apps.Select(app => app.Name));
    }

    [Fact]
    public void The_icon_comes_from_the_app_that_stands_for_the_company()
    {
        var company = Scan(App("Adobe Acrobat"), App("Adobe Creative Cloud")).Single(app => app.Name == "Adobe");

        Assert.Equal(@"D:\Apps\Adobe Creative Cloud.exe", company.Icons[0].Path);
    }

    [Fact]
    public void Without_that_app_any_of_the_company_s_apps_will_do()
    {
        var company = Scan(App("Adobe Photoshop 2026")).Single(app => app.Name == "Adobe");

        Assert.Equal(@"D:\Apps\Adobe Photoshop 2026.exe", company.Icons[0].Path);
    }

    [Fact]
    public void A_company_never_claims_an_install_folder()
    {
        var company = Scan(App("Google Chrome")).Single(app => app.Name == "Google");

        Assert.Null(company.InstallFolder);
        Assert.Equal("company:Google", company.Id);
    }

    [Fact]
    public void A_company_with_nothing_installed_has_no_item()
    {
        Assert.Equal(["Paper Boat"], Scan(App("Paper Boat")).Select(app => app.Name));
    }

    [Fact]
    public void A_name_that_only_starts_the_same_way_is_not_the_company()
    {
        Assert.Equal(["Adobeland"], Scan(App("Adobeland")).Select(app => app.Name));
    }

    [Fact]
    public void A_game_does_not_make_a_company()
    {
        Assert.Equal(["Google Quest"], Scan(App("Google Quest", isGame: true)).Select(app => app.Name));
    }

    [Fact]
    public void An_app_already_called_that_is_left_as_the_only_one()
    {
        var apps = Scan(App("Adobe"), App("Adobe Acrobat"));

        Assert.Equal(["Adobe", "Adobe Acrobat"], apps.Select(app => app.Name));
    }
}
