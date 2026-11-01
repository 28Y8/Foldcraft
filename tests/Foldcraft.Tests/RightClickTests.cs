using Foldcraft.Core.Shell;

namespace Foldcraft.Tests;

public sealed class RightClickMenuTests
{
    private const string Exe = @"C:\Program Files\Foldcraft\Foldcraft.exe";
    private const string CommandKey = RightClickMenu.VerbKey + @"\command";

    private readonly FakeRegistry _registry = new();
    private readonly RightClickMenu _menu;

    public RightClickMenuTests()
    {
        _menu = new RightClickMenu(_registry);
    }

    [Fact]
    public void The_entry_goes_under_the_current_users_directory_shell()
    {
        Assert.StartsWith(@"Software\Classes\Directory\shell\", RightClickMenu.VerbKey);
    }

    [Fact]
    public void The_command_quotes_both_the_app_and_the_folder()
    {
        Assert.Equal($"\"{Exe}\" --customize \"%1\"", RightClickMenu.BuildCommand(Exe));
    }

    [Fact]
    public void A_custom_data_folder_travels_with_the_command()
    {
        Assert.Equal(
            $"\"{Exe}\" --customize \"%1\" --data-dir \"C:\\Trial\"",
            RightClickMenu.BuildCommand(Exe, @"C:\Trial\"));
    }

    [Theory]
    [InlineData("Foldcraft.exe")]
    [InlineData(@"..\Foldcraft.exe")]
    [InlineData("C:\\Odd\"name\\Foldcraft.exe")]
    [InlineData(@"C:\50%off\Foldcraft.exe")]
    [InlineData("C:\\Line\nbreak\\Foldcraft.exe")]
    public void A_path_that_cannot_be_quoted_safely_is_refused(string path)
    {
        Assert.Null(RightClickMenu.BuildCommand(path));
        Assert.Equal(RightClickOutcome.CannotQuote, _menu.Enable(path).Outcome);
        Assert.Equal(0, _registry.Writes);
    }

    [Fact]
    public void Turning_it_on_adds_one_entry()
    {
        var result = _menu.Enable(Exe);

        Assert.Equal(RightClickOutcome.Added, result.Outcome);
        Assert.Equal(RightClickMenu.BuildCommand(Exe), result.Command);
        Assert.Equal(RightClickMenu.MenuText, _registry.GetString(RightClickMenu.VerbKey, null));
        Assert.Equal(result.Command, _registry.GetString(CommandKey, null));
        Assert.Equal([RightClickMenu.VerbKey, CommandKey], _registry.Keys.Order());
    }

    [Fact]
    public void Turning_it_on_twice_is_harmless()
    {
        _menu.Enable(Exe);

        Assert.Equal(RightClickOutcome.Added, _menu.Enable(Exe).Outcome);
    }

    [Fact]
    public void Turning_it_off_removes_exactly_what_was_added()
    {
        _registry.SetString(@"Software\Classes\Directory\shell\Other", null, "Someone else's");
        var added = _menu.Enable(Exe);

        var result = _menu.Disable(added.Command!, Exe);

        Assert.Equal(RightClickOutcome.Removed, result.Outcome);
        Assert.False(_registry.KeyExists(RightClickMenu.VerbKey));
        Assert.False(_registry.KeyExists(CommandKey));
        Assert.Equal("Someone else's", _registry.GetString(@"Software\Classes\Directory\shell\Other", null));
    }

    [Fact]
    public void A_value_someone_else_added_stays_and_so_does_its_key()
    {
        var added = _menu.Enable(Exe);
        _registry.SetString(RightClickMenu.VerbKey, "Extended", "yes");

        Assert.Equal(RightClickOutcome.Removed, _menu.Disable(added.Command!, Exe).Outcome);

        Assert.Equal("yes", _registry.GetString(RightClickMenu.VerbKey, "Extended"));
        Assert.Null(_registry.GetString(RightClickMenu.VerbKey, null));
        Assert.False(_registry.KeyExists(CommandKey));
    }

    [Fact]
    public void An_entry_that_changed_is_left_alone()
    {
        var added = _menu.Enable(Exe);
        _registry.SetString(CommandKey, null, "other.exe \"%1\"");

        Assert.Equal(RightClickOutcome.LeftAlone, _menu.Disable(added.Command!, Exe).Outcome);
        Assert.Equal("other.exe \"%1\"", _registry.GetString(CommandKey, null));
    }

    [Fact]
    public void Something_else_already_using_that_name_is_left_alone()
    {
        _registry.SetString(CommandKey, null, "other.exe \"%1\"");

        Assert.Equal(RightClickOutcome.LeftAlone, _menu.Enable(Exe).Outcome);
        Assert.Equal("other.exe \"%1\"", _registry.GetString(CommandKey, null));
    }

    [Fact]
    public void Turning_off_with_nothing_there_does_nothing()
    {
        Assert.Equal(RightClickOutcome.NothingThere, _menu.Disable("x", Exe).Outcome);
        Assert.Equal(0, _registry.Writes);
    }

    [Fact]
    public void A_registry_that_refuses_is_a_failure_and_not_a_crash()
    {
        _registry.FailWrites = true;

        Assert.Equal(RightClickOutcome.Failed, _menu.Enable(Exe).Outcome);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_failure_partway_through_leaves_nothing_behind(int writesBeforeFailure)
    {
        _registry.WritesBeforeFailure = writesBeforeFailure;

        Assert.Equal(RightClickOutcome.Failed, _menu.Enable(Exe).Outcome);
        Assert.Empty(_registry.Keys);
    }

    private const string DirectoryKey = @"Software\Classes\Directory";
    private const string ShellKey = DirectoryKey + @"\shell";

    [Fact]
    public void Keys_made_above_the_entry_go_when_it_is_turned_off()
    {
        _registry.MakesParentKeys = true;

        var added = _menu.Enable(Exe);

        Assert.Equal([ShellKey, DirectoryKey], added.AddedParents);
        Assert.Contains(DirectoryKey, _registry.Keys);

        Assert.Equal(RightClickOutcome.Removed, _menu.Disable(added.Command!, Exe, added.AddedParents).Outcome);

        Assert.Empty(_registry.Keys);
    }

    [Fact]
    public void Keys_that_were_already_above_the_entry_stay()
    {
        _registry.MakesParentKeys = true;
        _registry.SetString(ShellKey + @"\Other", null, "Someone else's");

        var added = _menu.Enable(Exe);

        Assert.Empty(added.AddedParents);

        _menu.Disable(added.Command!, Exe, added.AddedParents);

        Assert.True(_registry.KeyExists(ShellKey));
        Assert.Equal("Someone else's", _registry.GetString(ShellKey + @"\Other", null));
    }

    [Fact]
    public void A_key_made_for_the_entry_stays_if_something_else_has_used_it_since()
    {
        _registry.MakesParentKeys = true;

        var added = _menu.Enable(Exe);

        _registry.SetString(DirectoryKey + @"\Background", null, "Someone else's");
        _menu.Disable(added.Command!, Exe, added.AddedParents);

        Assert.False(_registry.KeyExists(ShellKey));
        Assert.True(_registry.KeyExists(DirectoryKey));
    }

    [Fact]
    public void Only_keys_above_the_entry_can_be_taken_away()
    {
        var added = _menu.Enable(Exe);

        _registry.SetString(@"Software\Classes\Other", "x", "y");
        _registry.DeleteValue(@"Software\Classes\Other", "x");

        _menu.Disable(added.Command!, Exe, [@"Software\Classes\Other"]);

        Assert.Contains(@"Software\Classes\Other", _registry.Keys);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_failure_partway_through_leaves_no_parent_keys_behind(int writesBeforeFailure)
    {
        _registry.MakesParentKeys = true;
        _registry.WritesBeforeFailure = writesBeforeFailure;

        Assert.Equal(RightClickOutcome.Failed, _menu.Enable(Exe).Outcome);
        Assert.Empty(_registry.Keys);
    }

    [Fact]
    public void A_failure_rewriting_our_own_entry_does_not_take_it_away()
    {
        _menu.Enable(Exe);
        _registry.FailWrites = true;

        Assert.Equal(RightClickOutcome.Failed, _menu.Enable(Exe).Outcome);
        Assert.True(_registry.KeyExists(RightClickMenu.VerbKey));
    }
}

public sealed class AppHarnessSafetyTests
{
    [Fact]
    public void The_harness_never_holds_the_real_registry()
    {
        using var app = new AppHarness();

        Assert.IsType<FakeRegistry>(app.Registry);
        Assert.Equal(0, app.Registry.Reads + app.Registry.Writes);
    }
}
