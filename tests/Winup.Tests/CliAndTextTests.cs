using Winup.Cli;

namespace Winup.Tests;

[Collection("Loc")] // Loc.Lang is global
public class CliAndTextTests
{
    private static (int code, string output) Run(Fixture f, string input, params string[] args)
    {
        var o = new StringWriter();
        var cli = new CommandLine(f.Env, f.Bin, f.Probe, o, new StringReader(input), interactive: input.Length > 0, color: false);
        var code = cli.Run(args);
        Loc.Lang = "en";
        return (code, o.ToString());
    }

    [Fact]
    public void No_command_prints_help_and_exits_2()
    {
        using var f = new Fixture();
        var (code, output) = Run(f, "");
        Assert.Equal(2, code);
        Assert.Contains("winup check", output);
    }

    [Fact]
    public void Bad_options_and_unknown_commands()
    {
        using var f = new Fixture();
        Assert.Equal(2, Run(f, "", "--level", "huge", "check").code);
        Assert.Equal(2, Run(f, "", "--lang", "xx", "check").code);
        Assert.Equal(1, Run(f, "", "bogus").code);
    }

    [Theory]
    [InlineData("en", "What can be cleaned")]
    [InlineData("uk", "Що можна почистити")]
    public void Check_lists_candidates_in_every_language_and_deletes_nothing(string lang, string header)
    {
        using var f = new Fixture();
        var before = f.Snapshot();
        var (code, output) = Run(f, "", "check", "--lang", lang);
        Assert.Equal(0, code);
        Assert.Contains(header, output);
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public void Check_then_pick_then_no_changes_nothing()
    {
        using var f = new Fixture();
        var before = f.Snapshot();
        var (_, output) = Run(f, "1-3\nn\n", "check");
        Assert.Contains("Nothing was changed.", output);
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public void Check_then_pick_a_level_then_yes_cleans_that_level()
    {
        using var f = new Fixture();
        var (_, output) = Run(f, "safe\ny\n", "check");
        Assert.Contains("Moved to the Recycle Bin", output);
        Assert.False(File.Exists(f.P(Fixture.SafeDeletes[2])));
        Assert.True(Directory.Exists(f.P(Fixture.StandardDeletes[0])));
    }

    [Fact]
    public void Clean_without_yes_in_a_script_asks_for_it()
    {
        using var f = new Fixture();
        var before = f.Snapshot();
        var (code, _) = Run(f, "", "clean");
        Assert.Equal(2, code);
        Assert.Equal(before, f.Snapshot());
    }

    [Fact]
    public void Demo_lists_sample_data_and_its_cleanup_changes_nothing()
    {
        using var f = new Fixture();
        var before = f.Snapshot();
        var (code, output) = Run(f, "", "clean", "--demo", "--yes", "--level", "aggressive");
        Assert.Equal(0, code);
        Assert.Contains("Visual Studio Code caches", output);
        Assert.Contains("Nothing was changed.", output);
        Assert.Equal(before, f.Snapshot());
        Assert.Empty(f.Bin.Recycled);
        Assert.False(Directory.Exists(f.Env.DataDir)); // no report or history either
    }

    [Fact]
    public void Pick_understands_numbers_ranges_and_levels()
    {
        using var f = new Fixture();
        var shown = f.Check().ToList();
        Assert.Equal(3, CommandLine.Pick("1, 2-3", shown).Count);
        Assert.Equal(shown.Count(c => c.InLevel(Level.Safe)), CommandLine.Pick("safe", shown).Count);
        Assert.Empty(CommandLine.Pick("", shown));
        Assert.Empty(CommandLine.Pick("999", shown));
    }

    [Fact]
    public void Every_string_has_ukrainian_and_russian()
    {
        var rules = RuleSet.LoadDefault().Rules;
        var keys = rules.SelectMany(r => new[] { r.Label, r.Note }).OfType<string>()
            .Concat(["{0} caches", "{0} cache", "safe", "standard", "aggressive", "B", "KB", "MB", "GB", "TB"])
            .Concat(Loc.Keys);
        var missing = Loc.Missing(keys).ToList();
        Assert.True(missing.Count == 0, "missing translations: " + string.Join(" | ", missing));
    }
}
