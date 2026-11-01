using Foldcraft.Core.Apps;

namespace Foldcraft.Tests;

public sealed class VdfParserTests
{
    [Fact]
    public void Nested_objects_are_read()
    {
        var root = VdfParser.Parse("\"AppState\"\n{\n\t\"appid\"\t\t\"440\"\n\t\"UserConfig\"\n\t{\n\t\t\"language\"\t\"english\"\n\t}\n}\n");

        Assert.Equal("440", root.Child("AppState")?.Text("appid"));
        Assert.Equal("english", root.Child("AppState")?.Child("UserConfig")?.Text("language"));
    }

    [Fact]
    public void Names_match_in_any_case()
    {
        Assert.Equal("1", VdfParser.Parse("\"Key\" \"1\"").Text("KEY"));
    }

    [Theory]
    [InlineData(@"""path"" ""D:\\Games\\Lib""", @"D:\Games\Lib")]
    [InlineData(@"""path"" ""say \""hi\""""", "say \"hi\"")]
    [InlineData(@"""path"" ""a\tb""", "a\tb")]
    [InlineData(@"""path"" ""a\nb""", "a\nb")]
    public void Escapes_are_understood(string text, string expected)
    {
        Assert.Equal(expected, VdfParser.Parse(text).Text("path"));
    }

    [Fact]
    public void A_backslash_that_escapes_nothing_stays_as_it_is()
    {
        Assert.Equal(@"C:\Games\Lib", VdfParser.Parse(@"""path"" ""C:\Games\Lib""").Text("path"));
    }

    [Fact]
    public void Comments_are_skipped()
    {
        var root = VdfParser.Parse("// a note\n\"a\" \"1\" // another\n\"b\" \"2\"\n");

        Assert.Equal("1", root.Text("a"));
        Assert.Equal("2", root.Text("b"));
    }

    [Fact]
    public void A_slash_inside_quotes_is_not_a_comment()
    {
        Assert.Equal("steam://run//1", VdfParser.Parse("\"a\" \"steam://run//1\"").Text("a"));
    }

    [Fact]
    public void A_byte_order_mark_is_ignored()
    {
        Assert.Equal("1", VdfParser.Parse("\uFEFF\"a\" \"1\"").Text("a"));
    }

    [Fact]
    public void Unquoted_tokens_are_read()
    {
        var root = VdfParser.Parse("AppState\n{\nappid 440\n}\n");

        Assert.Equal("440", root.Child("AppState")?.Text("appid"));
    }

    [Fact]
    public void A_platform_condition_after_a_value_is_skipped()
    {
        var root = VdfParser.Parse("\"a\" \"1\" [$WIN32]\n\"b\" \"2\"\n");

        Assert.Equal("1", root.Text("a"));
        Assert.Equal("2", root.Text("b"));
    }

    [Fact]
    public void A_file_cut_off_inside_an_object_keeps_what_came_first()
    {
        var root = VdfParser.Parse("\"AppState\"\n{\n\"appid\" \"440\"\n\"name\" \"Sam");

        Assert.Equal("440", root.Child("AppState")?.Text("appid"));
        Assert.Null(root.Child("AppState")?.Text("name"));
    }

    [Fact]
    public void A_stray_closing_brace_changes_nothing()
    {
        var root = VdfParser.Parse("}\n\"a\" \"1\"\n}\n\"b\" \"2\"");

        Assert.Equal("1", root.Text("a"));
        Assert.Equal("2", root.Text("b"));
    }

    [Fact]
    public void A_repeated_key_keeps_the_first()
    {
        Assert.Equal("1", VdfParser.Parse("\"a\" \"1\" \"a\" \"2\"").Text("a"));
    }

    [Fact]
    public void A_key_with_no_value_is_dropped()
    {
        var root = VdfParser.Parse("{ \"a\" } \"b\" \"2\"");

        Assert.Equal("2", root.Text("b"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t ")]
    [InlineData("\"")]
    [InlineData("{{{{")]
    [InlineData("}}}}")]
    [InlineData("[$")]
    [InlineData("\0\0\u0001\u0002 junk \\ \"")]
    public void Nonsense_gives_an_empty_or_partial_result_rather_than_an_error(string text)
    {
        Assert.NotNull(VdfParser.Parse(text));
    }

    [Fact]
    public void Extreme_nesting_is_cut_off_rather_than_overflowing_the_stack()
    {
        var text = string.Concat(Enumerable.Repeat("\"a\" {", 500_000)) + "\"deep\" \"1\"";

        var root = VdfParser.Parse(text);

        Assert.NotNull(root.Child("a"));
    }

    [Fact]
    public void Reading_carries_on_after_nesting_that_was_cut_off()
    {
        var deep = string.Concat(Enumerable.Repeat("\"a\" {", 100)) + string.Concat(Enumerable.Repeat("}", 100));

        Assert.Equal("1", VdfParser.Parse(deep + "\"after\" \"1\"").Text("after"));
    }

    [Fact]
    public void A_missing_file_gives_null()
    {
        Assert.Null(VdfParser.TryParseFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "absent.vdf")));
    }
}
