using VacationCalculator.App.Services;
using Xunit.Abstractions;

namespace VacationCalculator.App.Tests;

public class DataPathResolverTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vc-path-" + Guid.NewGuid().ToString("N"));

    public DataPathResolverTests(ITestOutputHelper output)
    {
        _out = output;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t ")]
    public void Rejects_null_empty_whitespace(string? value) => Assert.Null(DataPathResolver.Resolve(value));

    [Theory]
    [InlineData(@"C:\x:y.json")]
    [InlineData("a.json::$DATA")]
    [InlineData("a.json:stream")]
    [InlineData(@"C:\dir\a.json:")]
    [InlineData(@":a.json")]
    [InlineData(@"1:\a.json")]
    [InlineData(@"\\?\C:\a.json")]
    [InlineData(@"CC:\a.json")]
    [InlineData(@"C::\a.json")]
    [InlineData(@"Æ:\a.json")]
    [InlineData(@"C:\dir:name\a.json")]
    public void Rejects_misplaced_colon(string value) => Assert.Null(DataPathResolver.Resolve(value));

    [Theory]
    [InlineData("a*.json")]
    [InlineData("a?.json")]
    [InlineData(@"C:\*\a.json")]
    [InlineData(@"C:\dir?\a.json")]
    [InlineData("*")]
    public void Rejects_wildcards(string value) => Assert.Null(DataPathResolver.Resolve(value));

    [Theory]
    [InlineData("a|b.json")]
    [InlineData("a<b.json")]
    [InlineData("a>b.json")]
    [InlineData("a\"b.json")]
    [InlineData(@"C:\dir|x\a.json")]
    [InlineData(@"C:\dir\a|b.json")]
    [InlineData("a\0b.json")]
    [InlineData("a\tb.json")]
    [InlineData("a\nb.json")]
    public void Rejects_invalid_characters(string value) => Assert.Null(DataPathResolver.Resolve(value));

    [Theory]
    [InlineData(@"C:\Temp\")]
    [InlineData(@"C:\Temp/")]
    [InlineData(@"C:\")]
    [InlineData(@"\\server\share\")]
    [InlineData("folder\\")]
    [InlineData("folder/")]
    public void Rejects_empty_file_name_trailing_slash(string value) => Assert.Null(DataPathResolver.Resolve(value));

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(@"C:\Temp\..")]
    public void Rejects_dot_segments_that_resolve_to_directory(string value) => Assert.Null(DataPathResolver.Resolve(value));

    [Fact]
    public void Rejects_existing_directory_without_trailing_slash()
    {
        Assert.Null(DataPathResolver.Resolve(_dir));
        Assert.Null(DataPathResolver.Resolve(_dir + "\\.."));
    }

    [Fact]
    public void Rejects_existing_directory_given_relative()
    {
        var sub = Path.Combine(_dir, "sub");
        Directory.CreateDirectory(sub);
        var old = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_dir);
            Assert.Null(DataPathResolver.Resolve("sub"));
            Assert.Equal(Path.Combine(_dir, "sub2.json"), DataPathResolver.Resolve("sub2.json"));
        }
        finally { Directory.SetCurrentDirectory(old); }
    }

    [Fact]
    public void Accepts_absolute_path_to_missing_file_and_existing_file()
    {
        var p = Path.Combine(_dir, "data.json");
        Assert.Equal(p, DataPathResolver.Resolve(p));
        File.WriteAllText(p, "{}");
        Assert.Equal(p, DataPathResolver.Resolve(p));
        Assert.Equal("{}", File.ReadAllText(p)); // must not modify the file
    }

    [Fact]
    public void Does_not_create_anything()
    {
        var p = Path.Combine(_dir, "newdir", "data.json");
        Assert.Equal(p, DataPathResolver.Resolve(p));
        Assert.False(Directory.Exists(Path.Combine(_dir, "newdir")));
    }

    [Fact]
    public void Accepts_relative_path_resolved_against_cwd()
    {
        var old = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_dir);
            Assert.Equal(Path.Combine(_dir, "data.json"), DataPathResolver.Resolve("data.json"));
            Assert.Equal(Path.Combine(_dir, "sub", "data.json"), DataPathResolver.Resolve(@".\sub\data.json"));
            Assert.Equal(Path.Combine(Path.GetDirectoryName(_dir)!, "data.json"), DataPathResolver.Resolve(@"..\data.json"));
        }
        finally { Directory.SetCurrentDirectory(old); }
    }

    [Fact]
    public void Accepts_spaces_and_non_ascii()
    {
        Assert.Equal(@"C:\Users\Æble Ø\data.json", DataPathResolver.Resolve(@"C:\Users\Æble Ø\data.json"));
        Assert.Equal(@"C:\My Docs\my data file.json", DataPathResolver.Resolve(@"C:\My Docs\my data file.json"));
        Assert.Equal(@"C:\日本語\データ.json", DataPathResolver.Resolve(@"C:\日本語\データ.json"));
    }

    [Fact]
    public void Accepts_unc_path()
    {
        Assert.Equal(@"\\server\share\data.json", DataPathResolver.Resolve(@"\\server\share\data.json"));
        Assert.Equal(@"\\server\share\sub dir\data.json", DataPathResolver.Resolve(@"\\server\share\sub dir\data.json"));
    }

    [Fact]
    public void Accepts_lowercase_drive_letter()
    {
        var r = DataPathResolver.Resolve(@"c:\temp\data.json");
        Assert.NotNull(r);
        Assert.Equal(@"c:\temp\data.json", r, ignoreCase: true);
    }

    [Fact]
    public void Accepts_forward_slashes_normalised()
    {
        Assert.Equal(@"C:\Temp\sub\data.json", DataPathResolver.Resolve("C:/Temp/sub/data.json"));
    }

    [Fact]
    public void Collapses_dot_segments()
    {
        Assert.Equal(@"C:\Temp\data.json", DataPathResolver.Resolve(@"C:\Temp\a\..\.\data.json"));
    }

    [Fact]
    public void Drive_relative_path_is_accepted_and_resolves_to_a_full_path()
    {
        var r = DataPathResolver.Resolve("C:data.json");
        Assert.NotNull(r);
        Assert.True(Path.IsPathFullyQualified(r!));
        Assert.Equal("data.json", Path.GetFileName(r));
    }

    [Theory]
    [InlineData(@"C:\Temp\data.json ")]
    [InlineData(@"C:\Temp\data.json.")]
    [InlineData(@"C:\Temp\data.json. .")]
    public void Trailing_space_or_dot_is_accepted_and_stripped_by_windows(string value)
    {
        // Win32 normalisation strips trailing spaces/dots, so the app opens data.json; document the result.
        var r = DataPathResolver.Resolve(value);
        _out.WriteLine($"[{value}] -> [{r}]");
        Assert.Equal(@"C:\Temp\data.json", r);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("prn")]
    [InlineData("CON.json")]
    [InlineData(@"C:\Temp\NUL")]
    [InlineData(@"C:\Temp\AUX.json")]
    public void Reserved_device_names_report(string value)
    {
        // Observation only: record how they are treated.
        var r = DataPathResolver.Resolve(value);
        _out.WriteLine($"[{value}] -> {(r is null ? "null (rejected)" : "[" + r + "]")}");
    }

    [Fact]
    public void Very_long_directory_path_does_not_throw()
    {
        var p = @"C:\" + string.Join(@"\", Enumerable.Repeat(new string('a', 50), 8)) + @"\data.json"; // ~410 chars
        Assert.Equal(p, DataPathResolver.Resolve(p));
    }

    [Fact]
    public void Absurdly_long_path_is_rejected_or_resolved_without_throwing()
    {
        var p = @"C:\" + new string('a', 40000) + @"\data.json";
        var r = DataPathResolver.Resolve(p);
        _out.WriteLine(r is null ? "null" : "length " + r.Length);
    }

    [Fact]
    public void File_name_over_255_chars_report()
    {
        var p = @"C:\Temp\" + new string('a', 300) + ".json";
        var r = DataPathResolver.Resolve(p);
        _out.WriteLine(r is null ? "null (rejected)" : "accepted, file name length " + Path.GetFileName(r).Length);
    }
}
