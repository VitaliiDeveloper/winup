namespace Winup.Tests;

public class CleanupTests
{
    private static IEnumerable<string> Deleted(Fixture f, SortedSet<string> before) =>
        before.Except(f.Snapshot(), StringComparer.OrdinalIgnoreCase);

    private static void AssertExactly(Fixture f, SortedSet<string> before, IEnumerable<string> expected)
    {
        var exp = expected.ToList();
        foreach (var e in exp) Assert.False(Path.Exists(f.P(e)), "should be deleted: " + e);
        foreach (var p in Fixture.Precious) Assert.True(File.Exists(f.P(p)), "must survive: " + p);
        var extra = Deleted(f, before).Where(d => !exp.Any(e => d.Equals(e, StringComparison.OrdinalIgnoreCase) || d.StartsWith(e + "\\", StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.True(extra.Count == 0, "deleted unexpectedly: " + string.Join(", ", extra));
    }

    [Fact]
    public void Check_changes_nothing()
    {
        using var f = new Fixture();
        var before = f.Snapshot();
        var found = f.Check();
        Assert.NotEmpty(found);
        Assert.Equal(before, f.Snapshot());
        Assert.Empty(f.Bin.Recycled);
    }

    [Theory]
    [InlineData(Level.Safe)]
    [InlineData(Level.Standard)]
    [InlineData(Level.Aggressive)]
    public void Each_level_deletes_exactly_its_set(Level level)
    {
        using var f = new Fixture();
        var before = f.Snapshot();
        var chosen = f.Check().Where(c => c.InLevel(level)).ToList();
        var result = f.Cleaner().Clean(chosen);
        var expected = Fixture.SafeDeletes.AsEnumerable();
        if (level >= Level.Standard) expected = expected.Concat(Fixture.StandardDeletes);
        if (level >= Level.Aggressive) expected = expected.Concat(Fixture.AggressiveDeletes);
        AssertExactly(f, before, expected);
        Assert.True(result.FreedBytes > 0);
    }

    [Fact]
    public void Junction_inside_a_cache_is_neither_followed_nor_removed()
    {
        using var f = new Fixture();
        f.Cleaner().Clean(f.Check().Where(c => c.Id == "gpu.directx"));
        Assert.True(File.Exists(Path.Combine(f.Elsewhere, "precious.txt")));
        Assert.True(Directory.Exists(Path.Combine(f.L, "D3DSCache", "link-out")));
        Assert.False(File.Exists(Path.Combine(f.L, "D3DSCache", "shader1")));
    }

    [Fact]
    public void Folder_on_the_way_being_a_junction_means_nothing_is_found_or_deleted()
    {
        using var f = new Fixture();
        var target = Path.Combine(f.Elsewhere, "amd");
        Directory.CreateDirectory(Path.Combine(target, "DxCache"));
        f.Pad(Path.Combine(target, "DxCache", "big.bin"), 3);
        Fixture.Junction(Path.Combine(f.L, "AMD"), target);
        Assert.DoesNotContain(f.Check(), c => c.Id == "gpu.amd");
        Assert.True(File.Exists(Path.Combine(target, "DxCache", "big.bin")));
    }

    [Fact]
    public void Paths_are_checked_again_right_before_deleting()
    {
        using var f = new Fixture();
        var chosen = f.Check().Where(c => c.Id == "gpu.nvidia").ToList();
        Assert.Single(chosen);
        // between check and clean, the cache folder is swapped for a junction to precious data
        var dx = Path.Combine(f.L, "NVIDIA", "DXCache");
        Directory.Delete(dx, true);
        var precious = Path.Combine(f.Elsewhere, "swap");
        Directory.CreateDirectory(precious);
        File.WriteAllText(Path.Combine(precious, "keep.txt"), "x");
        Fixture.Junction(dx, precious);
        f.Cleaner().Clean(chosen);
        Assert.True(File.Exists(Path.Combine(precious, "keep.txt")));
    }

    [Fact]
    public void Open_app_is_skipped_at_check_and_at_clean()
    {
        using var f = new Fixture();
        f.Probe.Running.Add("Code");
        var code = f.Check().Single(c => c.Id == "apps.electron.visualstudiocode");
        Assert.False(code.Selectable);
        Assert.False(code.InLevel(Level.Aggressive));
        f.Probe.Running.Clear();
        var fresh = f.Check().Single(c => c.Id == "apps.electron.visualstudiocode");
        f.Probe.Running.Add("Code"); // opened after the check
        var result = f.Cleaner().Clean([fresh]);
        Assert.StartsWith("open:", result.Items.Single().SkippedBecause);
        Assert.True(Directory.Exists(Path.Combine(f.R, "Code", "Cache", "Cache_Data")));
    }

    [Fact]
    public void Admin_rules_are_listed_but_not_cleaned_without_admin()
    {
        using var f = new Fixture();
        var win = f.Check().Single(c => c.Id == "temp.windows");
        Assert.True(win.NeedsAdmin);
        Assert.False(win.Selectable);
        var result = f.Cleaner().Clean([win]);
        Assert.NotNull(result.Items.Single().SkippedBecause);
        Assert.True(File.Exists(Path.Combine(f.W, "Temp", "old-sys.tmp")));
    }

    [Fact]
    public void Manual_rules_are_never_chosen_by_a_level_but_can_be_picked()
    {
        using var f = new Fixture();
        var nuget = f.Check().Single(c => c.Id == "dev.nuget_packages");
        Assert.True(nuget.Selectable);
        Assert.False(nuget.InLevel(Level.Aggressive));
        f.Cleaner().Clean([nuget]);
        Assert.False(Directory.Exists(Path.Combine(f.U, ".nuget", "packages", "newtonsoft.json")));
        Assert.True(Directory.Exists(Path.Combine(f.U, ".nuget", "packages")));
    }

    [Fact]
    public void Recent_temp_files_stay()
    {
        using var f = new Fixture();
        var temp = f.Check().Single(c => c.Id == "temp.user");
        f.Cleaner().Clean([temp]);
        Assert.True(File.Exists(Path.Combine(f.L, "Temp", "fresh.tmp")));
        Assert.False(File.Exists(Path.Combine(f.L, "Temp", "old-installer.tmp")));
    }

    [Fact]
    public void Unknown_chromium_app_is_found_by_its_folder_layout()
    {
        using var f = new Fixture();
        var some = f.Check().Single(c => c.Rule.LabelArg == "SomeApp");
        Assert.Equal(Level.Safe, some.Level);
        Assert.True(some.Rule.ProbeOpenFiles);
        // with bookmarks it is a browser: aggressive
        f.Make(@"Users\me\AppData\Roaming\SomeApp\Default\Bookmarks", 0);
        Assert.Equal(Level.Aggressive, f.Check().Single(c => c.Rule.LabelArg == "SomeApp").Level);
    }

    [Fact]
    public void Paths_owned_by_a_table_rule_are_not_listed_twice()
    {
        using var f = new Fixture();
        var all = f.Check();
        var targets = all.SelectMany(c => c.Targets).ToList();
        Assert.Equal(targets.Count, targets.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(all, c => c.Id.StartsWith("apps.found") && c.Rule.LabelArg is "Chrome" or "Code");
    }

    [Fact]
    public void A_cleanup_writes_a_report_and_history()
    {
        using var f = new Fixture();
        var result = f.Cleaner().Clean(f.Check().Where(c => c.InLevel(Level.Safe)));
        var store = new HistoryStore(f.Env);
        var entry = store.Save(result);
        Assert.True(File.Exists(entry.Report));
        Assert.Single(store.Load());
        Assert.StartsWith(f.Root, entry.Report);
    }
}
