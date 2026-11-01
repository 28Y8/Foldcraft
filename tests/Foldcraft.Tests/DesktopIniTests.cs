using System.Text;
using Foldcraft.Core.Infrastructure;
using Foldcraft.Core.Ini;

namespace Foldcraft.Tests;

public sealed class DesktopIniTests : IDisposable
{
    private const string Foreign =
        "; a comment Foldcraft does not own\r\n" +
        "\r\n" +
        "[.ShellClassInfo]\r\n" +
        "LocalizedResourceName=@%SystemRoot%\\system32\\shell32.dll,-21770\r\n" +
        "InfoTip=Größe für Prüfung\r\n" +
        "IconResource=C:\\old\\icon.ico,3\r\n" +
        "IconIndex=-235\r\n" +
        "\r\n" +
        "[ViewState]\r\n" +
        "Mode=\r\n" +
        "Vid=\r\n" +
        "FolderType=Pictures\r\n" +
        "\r\n" +
        "[Something.Else]\r\n" +
        "a line with no equals sign\r\n" +
        "  ; an indented comment\r\n" +
        "  Spaced Key  =  spaced value  \r\n";

    private static readonly string Edited = Foreign.Replace("C:\\old\\icon.ico,3", "D:\\new\\ñandú.ico,0");

    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private static byte[] Encode(DesktopIniEncoding encoding, string text)
    {
        var win1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

        return encoding switch
        {
            DesktopIniEncoding.Utf16LittleEndianBom => [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(text)],
            DesktopIniEncoding.Utf16BigEndianBom => [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(text)],
            DesktopIniEncoding.Utf16LittleEndianNoBom => Encoding.Unicode.GetBytes(text),
            DesktopIniEncoding.Utf8Bom => [0xEF, 0xBB, 0xBF, .. new UTF8Encoding(false).GetBytes(text)],
            DesktopIniEncoding.Utf8NoBom => new UTF8Encoding(false).GetBytes(text),
            DesktopIniEncoding.Ansi => win1252.GetBytes(text),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
        };
    }

    public static TheoryData<DesktopIniEncoding> Encodings() =>
    [
        DesktopIniEncoding.Utf16LittleEndianBom,
        DesktopIniEncoding.Utf16BigEndianBom,
        DesktopIniEncoding.Utf16LittleEndianNoBom,
        DesktopIniEncoding.Utf8Bom,
        DesktopIniEncoding.Utf8NoBom,
        DesktopIniEncoding.Ansi,
    ];

    [Theory]
    [MemberData(nameof(Encodings))]
    public void Every_encoding_is_detected_and_decoded_exactly(DesktopIniEncoding encoding)
    {
        var document = DesktopIni.Parse(Encode(encoding, Foreign), ansiCodePage: 1252);

        Assert.Equal(encoding, document.SourceEncoding);
        Assert.Equal(Foreign, document.ToText());
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void Round_trip_changes_only_the_owned_key_and_writes_utf16(DesktopIniEncoding encoding)
    {
        var document = DesktopIni.Parse(Encode(encoding, Foreign), ansiCodePage: 1252);

        document.SetValue(".ShellClassInfo", "IconResource", "D:\\new\\ñandú.ico,0");
        var bytes = document.ToBytes();

        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xFE, bytes[1]);
        Assert.Equal(Edited, Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2));

        var reread = DesktopIni.Parse(bytes);
        Assert.Equal(DesktopIniEncoding.Utf16LittleEndianBom, reread.SourceEncoding);
        Assert.Equal("D:\\new\\ñandú.ico,0", reread.GetValue(".ShellClassInfo", "IconResource"));
        Assert.Equal("Größe für Prüfung", reread.GetValue(".SHELLCLASSINFO", "infotip"));
        Assert.Equal("Pictures", reread.GetValue("ViewState", "FolderType"));
        Assert.Equal("spaced value", reread.GetValue("Something.Else", "Spaced Key"));
    }

    [Fact]
    public void A_non_ascii_icon_path_survives_a_round_trip_through_disk()
    {
        var folder = _box.NewFolder("disk-trip");
        var path = Sandbox.IniPath(folder);
        const string icon = @"C:\Üsers\日本語\アイコン 📁.ico,0";

        var document = DesktopIni.CreateEmpty();
        document.SetValue(".ShellClassInfo", "IconResource", icon);
        document.Save(path);

        Assert.Equal(icon, DesktopIni.Load(path).GetValue(".ShellClassInfo", "IconResource"));
    }

    [Fact]
    public void A_new_key_joins_the_end_of_its_section()
    {
        var document = DesktopIni.Parse(Encoding.UTF8.GetBytes(
            "[.ShellClassInfo]\r\nInfoTip=hello\r\n\r\n[ViewState]\r\nMode=\r\n"));

        document.SetValue(".ShellClassInfo", "IconResource", "a.ico,0");

        Assert.Equal(
            "[.ShellClassInfo]\r\nInfoTip=hello\r\nIconResource=a.ico,0\r\n\r\n[ViewState]\r\nMode=\r\n",
            document.ToText());
    }

    [Fact]
    public void A_new_section_goes_last_and_leaves_the_rest_alone()
    {
        var document = DesktopIni.Parse(Encoding.UTF8.GetBytes("[ViewState]\r\nMode=\r\n"));

        document.SetValue(".ShellClassInfo", "IconResource", "a.ico,0");

        Assert.Equal("[ViewState]\r\nMode=\r\n[.ShellClassInfo]\r\nIconResource=a.ico,0\r\n", document.ToText());
    }

    [Fact]
    public void An_empty_document_gets_a_minimal_file()
    {
        var document = DesktopIni.CreateEmpty();

        document.SetValue(".ShellClassInfo", "IconResource", "a.ico,0");

        Assert.Equal("[.ShellClassInfo]\r\nIconResource=a.ico,0\r\n", document.ToText());
    }

    [Fact]
    public void Line_endings_are_preserved_per_line()
    {
        const string text = "[A]\nkey=1\r\nother=2\rlast=3";

        Assert.Equal(text, DesktopIni.Parse(Encoding.UTF8.GetBytes(text)).ToText());
    }

    [Fact]
    public void A_file_without_a_final_newline_gets_one_only_where_needed()
    {
        var document = DesktopIni.Parse(Encoding.UTF8.GetBytes("[.ShellClassInfo]\nInfoTip=x"));

        document.SetValue(".ShellClassInfo", "IconResource", "a.ico,0");

        Assert.Equal("[.ShellClassInfo]\nInfoTip=x\nIconResource=a.ico,0\n", document.ToText());
    }

    [Fact]
    public void Editing_keeps_the_original_key_spelling_and_indent()
    {
        var document = DesktopIni.Parse(Encoding.UTF8.GetBytes("[.shellclassinfo]\n  iconresource = old\n"));

        document.SetValue(".ShellClassInfo", "IconResource", "new");

        Assert.Equal("[.shellclassinfo]\n  iconresource =new\n", document.ToText());
    }

    [Fact]
    public void A_duplicate_key_keeps_its_later_copies()
    {
        var document = DesktopIni.Parse(Encoding.UTF8.GetBytes("[S]\nk=1\nk=2\n"));

        document.SetValue("S", "k", "9");

        Assert.Equal("[S]\nk=9\nk=2\n", document.ToText());
    }

    [Fact]
    public void A_key_in_another_section_is_not_confused_with_ours()
    {
        var document = DesktopIni.Parse(Encoding.UTF8.GetBytes("[Other]\nIconResource=theirs\n"));

        document.SetValue(".ShellClassInfo", "IconResource", "ours");

        Assert.Equal("theirs", document.GetValue("Other", "IconResource"));
        Assert.Equal("ours", document.GetValue(".ShellClassInfo", "IconResource"));
    }

    [Fact]
    public void A_value_that_would_inject_a_line_is_rejected()
    {
        var document = DesktopIni.CreateEmpty();

        Assert.Throws<ArgumentException>(() => document.SetValue("S", "k", "a\r\n[Evil]"));
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x41, 0x00 })]
    [InlineData(new byte[] { 0x41, 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x00 })]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x00, 0xD8 })]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0xC3, 0x28 })]
    public void Undecodable_bytes_are_refused_rather_than_mangled(byte[] bytes)
    {
        Assert.Throws<DesktopIniFormatException>(() => DesktopIni.Parse(bytes));
    }

    [Fact]
    public void Ansi_bytes_a_code_page_cannot_represent_are_refused()
    {
        // 0x81 followed by 0x20 is not a valid Shift-JIS sequence.
        byte[] bytes = [0x5B, 0x53, 0x5D, 0x81, 0x20, 0xFD];

        Assert.Throws<DesktopIniFormatException>(() => DesktopIni.Parse(bytes, ansiCodePage: 932));
    }

    [Theory]
    [InlineData(936, new byte[] { 0xCC, 0xA8, 0xCD, 0xA8 }, "\u53F0\u901A")]
    [InlineData(932, new byte[] { 0xC3, 0xA9 }, "\uFF83\uFF69")]
    [InlineData(949, new byte[] { 0xC3, 0xA9 }, "\uCC55")]
    public void Double_byte_ansi_text_that_looks_like_utf8_is_read_as_windows_reads_it(int codePage, byte[] value, string expected)
    {
        byte[] bytes = [.. Encoding.ASCII.GetBytes("[.ShellClassInfo]\r\nInfoTip="), .. value, 0x0D, 0x0A];

        var document = DesktopIni.Parse(bytes, codePage);

        Assert.Equal(DesktopIniEncoding.Ansi, document.SourceEncoding);
        Assert.Equal(expected, document.GetValue(".ShellClassInfo", "InfoTip"));
    }

    [Fact]
    public void Plain_ascii_needs_no_code_page()
    {
        var document = DesktopIni.Parse(Encoding.ASCII.GetBytes("[A]\r\nb=1\r\n"), ansiCodePage: 936);

        Assert.Equal(DesktopIniEncoding.Utf8NoBom, document.SourceEncoding);
    }

    [Fact]
    public void Utf8_is_the_answer_when_the_system_code_page_is_utf8()
    {
        var document = DesktopIni.Parse(new UTF8Encoding(false).GetBytes("[A]\r\nb=é\r\n"), ansiCodePage: 65001);

        Assert.Equal(DesktopIniEncoding.Utf8NoBom, document.SourceEncoding);
        Assert.Equal("é", document.GetValue("A", "b"));
    }

    [Theory]
    [InlineData(FileAttributes.Hidden | FileAttributes.System)]
    [InlineData(FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly)]
    [InlineData(FileAttributes.ReadOnly)]
    [InlineData(FileAttributes.Archive)]
    public void Saving_over_an_existing_file_keeps_its_attributes(FileAttributes attributes)
    {
        var folder = _box.NewFolder("attrs-" + (int)attributes);
        var path = Sandbox.IniPath(folder);
        Sandbox.PlantIni(folder, Encoding.UTF8.GetBytes("[A]\nb=1\n"), attributes);

        var document = DesktopIni.Load(path);
        document.SetValue("A", "b", "2");
        document.Save(path);

        Assert.Equal(attributes, File.GetAttributes(path));
        Assert.Equal("2", DesktopIni.Load(path).GetValue("A", "b"));
    }

    [Fact]
    public void A_new_file_is_hidden_and_system()
    {
        var path = Sandbox.IniPath(_box.NewFolder("fresh"));

        DesktopIni.CreateEmpty().Save(path);

        Assert.Equal(FileAttributes.Hidden | FileAttributes.System, File.GetAttributes(path));
    }

    [Fact]
    public void A_failed_commit_leaves_the_original_intact()
    {
        var folder = _box.NewFolder("failed-commit");
        var path = Sandbox.IniPath(folder);
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        const FileAttributes attributes = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly;
        Sandbox.PlantIni(folder, original, attributes);

        var document = DesktopIni.Load(path);
        document.SetValue("A", "b", "2");

        Assert.Throws<IOException>(() => document.Save(path, operations: new FaultyOperations { FailCommit = _ => true }));

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(attributes, File.GetAttributes(path));
        Assert.Equal(["desktop.ini"], Directory.GetFiles(folder).Select(Path.GetFileName));
    }

    [Fact]
    public void A_failed_temp_write_leaves_the_original_intact()
    {
        var folder = _box.NewFolder("failed-write");
        var path = Sandbox.IniPath(folder);
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(folder, original);

        var document = DesktopIni.Load(path);
        document.SetValue("A", "b", "2");

        Assert.Throws<IOException>(() => document.Save(path, operations: new FaultyOperations { FailWrite = _ => true }));

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(FileAttributes.Hidden | FileAttributes.System, File.GetAttributes(path));
        Assert.Equal(["desktop.ini"], Directory.GetFiles(folder).Select(Path.GetFileName));
    }

    [Fact]
    public void A_partly_written_temp_file_is_cleaned_up_and_the_original_survives()
    {
        var folder = _box.NewFolder("partial-temp");
        var path = Sandbox.IniPath(folder);
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(folder, original);

        var ops = new PartialWrite();
        var document = DesktopIni.Load(path);
        document.SetValue("A", "b", "2");

        Assert.Throws<IOException>(() => document.Save(path, operations: ops));

        Assert.True(ops.WroteSomething);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(["desktop.ini"], Directory.GetFiles(folder).Select(Path.GetFileName));
    }

    private sealed class PartialWrite : FileOperations
    {
        public bool WroteSomething { get; private set; }

        public override void WriteNewFile(string path, byte[] bytes)
        {
            File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);
            WroteSomething = true;
            throw new IOException("Simulated disk full.");
        }
    }
}
