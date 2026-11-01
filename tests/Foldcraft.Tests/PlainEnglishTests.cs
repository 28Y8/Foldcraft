using Foldcraft.App.Services;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Safety;

namespace Foldcraft.Tests;

public sealed class PlainEnglishTests
{
    private static readonly string[] Jargon =
        ["desktop.ini", "journal", "reparse", "junction", "symlink", "symbolic", "UNC", "exception", "attribute", "ShellClassInfo"];

    private static void AssertPlain(string text)
    {
        Assert.False(string.IsNullOrWhiteSpace(text));

        foreach (var word in Jargon)
        {
            Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static TheoryData<FolderRefusal> Refusals() => new(Enum.GetValues<FolderRefusal>());

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Every_refusal_has_plain_wording(FolderRefusal refusal) => AssertPlain(PlainEnglish.Refusal(refusal));

    [Fact]
    public void Each_refusal_has_its_own_wording()
    {
        var wordings = Enum.GetValues<FolderRefusal>().Select(PlainEnglish.Refusal).ToArray();

        Assert.Equal(wordings.Length, wordings.Distinct().Count());
    }

    [Theory]
    [InlineData("desktop.ini contains binary data.")]
    [InlineData("desktop.ini in this folder is too large to back up safely.")]
    [InlineData("The undo journal could not be written, so the folder was left alone: disk full")]
    [InlineData("Access to the path 'C:\\x\\desktop.ini' is denied.")]
    [InlineData("This path goes through a junction, symbolic link or other reparse point.")]
    [InlineData("The operation failed with HRESULT 0x80070005.")]
    [InlineData("")]
    [InlineData(null)]
    public void Technical_failures_never_reach_the_user(string? message)
    {
        var failure = new FolderApplyResult { RequestedPath = "x", Outcome = ApplyOutcome.Failed, Message = message };

        AssertPlain(PlainEnglish.ApplyResult(failure));
        AssertPlain(PlainEnglish.RestoreResult(new FolderRestoreResult("x", RestoreOutcome.Failed, message)));
    }

    [Fact]
    public void A_readable_failure_is_passed_through()
    {
        var failure = new FolderApplyResult
        {
            RequestedPath = "x",
            Outcome = ApplyOutcome.Failed,
            Message = "The image file could not be found.",
        };

        Assert.Equal("The image file could not be found.", PlainEnglish.ApplyResult(failure));
    }

    public static TheoryData<ApplyOutcome> ApplyOutcomes() => new(Enum.GetValues<ApplyOutcome>());

    public static TheoryData<RestoreOutcome> RestoreOutcomes() => new(Enum.GetValues<RestoreOutcome>());

    [Theory]
    [MemberData(nameof(ApplyOutcomes))]
    public void Every_apply_outcome_is_plain_even_with_a_technical_message(ApplyOutcome outcome) =>
        AssertPlain(PlainEnglish.ApplyResult(new FolderApplyResult
        {
            RequestedPath = "x",
            Outcome = outcome,
            Message = "desktop.ini has changed since Foldcraft wrote it.",
        }));

    [Theory]
    [MemberData(nameof(RestoreOutcomes))]
    public void Every_restore_outcome_is_plain_even_with_a_technical_message(RestoreOutcome outcome) =>
        AssertPlain(PlainEnglish.RestoreResult(new FolderRestoreResult("x", outcome, "desktop.ini has changed since Foldcraft wrote it.")));

    [Fact]
    public void A_refused_result_uses_the_refusal_wording()
    {
        var applied = new FolderApplyResult
        {
            RequestedPath = "x",
            Outcome = ApplyOutcome.Refused,
            Refusal = FolderRefusal.ProgramFiles,
            Message = "This is inside the Program Files, which Foldcraft never changes.",
        };

        Assert.Equal(PlainEnglish.Refusal(FolderRefusal.ProgramFiles), PlainEnglish.ApplyResult(applied));
        Assert.Equal(
            PlainEnglish.Refusal(FolderRefusal.AppData),
            PlainEnglish.RestoreResult(new FolderRestoreResult("x", RestoreOutcome.Refused, "raw", FolderRefusal.AppData)));
    }

    [Theory]
    [InlineData(1, "1 folder")]
    [InlineData(0, "0 folders")]
    [InlineData(12, "12 folders")]
    public void Folder_counts_read_naturally(int count, string expected) =>
        Assert.Equal(expected, PlainEnglish.Folders(count));

    [Fact]
    public void Apply_summary_counts_each_kind_of_result()
    {
        FolderApplyResult Result(ApplyOutcome outcome) => new() { RequestedPath = "x", Outcome = outcome };

        var summary = PlainEnglish.ApplySummary(
        [
            Result(ApplyOutcome.Applied), Result(ApplyOutcome.Applied), Result(ApplyOutcome.Unchanged),
            Result(ApplyOutcome.Refused), Result(ApplyOutcome.Failed), Result(ApplyOutcome.Canceled),
        ]);

        Assert.Contains("Changed 2 folders.", summary);
        Assert.Contains("1 already had this look.", summary);
        Assert.Contains("2 couldn't be changed.", summary);
        Assert.Contains("1 skipped.", summary);
        Assert.Contains("File Explorer", summary);
    }

    [Fact]
    public void The_Explorer_hint_only_appears_when_something_changed()
    {
        var summary = PlainEnglish.ApplySummary([new FolderApplyResult { RequestedPath = "x", Outcome = ApplyOutcome.Refused }]);

        Assert.Equal("1 couldn't be changed.", summary);
    }

    [Fact]
    public void Restore_summary_counts_each_kind_of_result()
    {
        var summary = PlainEnglish.RestoreSummary(
        [
            new FolderRestoreResult("a", RestoreOutcome.Restored),
            new FolderRestoreResult("b", RestoreOutcome.AlreadyRestored),
            new FolderRestoreResult("c", RestoreOutcome.Forgotten),
            new FolderRestoreResult("d", RestoreOutcome.FolderMissing),
        ]);

        Assert.Equal("Put back 2 folders. Stopped tracking 1 folder. 1 folder couldn't be put back.", summary);
    }

    [Fact]
    public void Summaries_of_nothing_say_so()
    {
        Assert.Equal("Nothing to do.", PlainEnglish.ApplySummary([]));
        Assert.Equal("Nothing to put back.", PlainEnglish.RestoreSummary([]));
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(247, false)]
    [InlineData(248, true)]
    [InlineData(400, true)]
    public void Paths_too_long_for_Explorer_are_flagged(int length, bool expected) =>
        Assert.Equal(expected, PlainEnglish.MayNotShowInExplorer(new string('x', length)));

    private static FolderApplyResult AppliedAt(int length) => new()
    {
        RequestedPath = new string('x', length),
        ResolvedPath = new string('x', length),
        Outcome = ApplyOutcome.Applied,
    };

    [Fact]
    public void A_folder_too_long_for_Explorer_is_not_reported_as_simply_done()
    {
        var text = PlainEnglish.ApplyResult(AppliedAt(248));

        AssertPlain(text);
        Assert.Contains("can't show", text);
        Assert.Equal("Done.", PlainEnglish.ApplyResult(AppliedAt(247)));
    }

    [Fact]
    public void The_summary_does_not_promise_a_look_Windows_will_never_show()
    {
        var summary = PlainEnglish.ApplySummary([AppliedAt(248)]);

        Assert.Contains("Changed 1 folder.", summary);
        Assert.Contains("too long", summary);
        Assert.DoesNotContain("close and reopen", summary);
    }

    [Fact]
    public void The_summary_keeps_the_hint_for_folders_that_will_show_their_look()
    {
        var summary = PlainEnglish.ApplySummary([AppliedAt(248), AppliedAt(100)]);

        Assert.Contains("Changed 2 folders.", summary);
        Assert.Contains("too long", summary);
        Assert.Contains("close and reopen", summary);
    }

    /// <summary>Cloud-synced folders get the same treatment as any other now, so they don't take longer.</summary>
    [Fact]
    public void The_summary_makes_no_special_case_of_cloud_folders()
    {
        Assert.DoesNotContain("loud", PlainEnglish.ApplySummary([AppliedAt(100)]));
    }

    [Fact]
    public void Times_read_relative_to_today()
    {
        var zone = TimeZoneInfo.Utc;
        var now = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

        Assert.StartsWith("Today at ", PlainEnglish.When(now.AddHours(-11), now, zone));
        Assert.StartsWith("Yesterday at ", PlainEnglish.When(now.AddHours(-13), now, zone));
        Assert.Contains("2026", PlainEnglish.When(now.AddDays(-5), now, zone));
    }
}
