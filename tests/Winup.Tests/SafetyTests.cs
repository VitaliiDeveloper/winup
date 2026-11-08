namespace Winup.Tests;

public class SafetyTests
{
    [Theory]
    [InlineData(@"Users\me\Documents\thesis.docx", @"Users\me\Documents", "not in an allowed place")]
    [InlineData(@"Users\me\.ssh\id_ed25519", @"Users\me\.ssh", "not in an allowed place")]
    [InlineData(@"Users\me\AppData\Roaming\Code\User\settings.json", @"Users\me\AppData\Roaming\Code", "protected name 'User'")]
    [InlineData(@"Users\me\AppData\Roaming\Code\Backups\unsaved.txt", @"Users\me\AppData\Roaming\Code", "protected name 'Backups'")]
    [InlineData(@"Users\me\AppData\Local\Google\Chrome\User Data\Default\Cookies", @"Users\me\AppData\Local\Google\Chrome\User Data\Default", "protected name 'User Data'")]
    [InlineData(@"Users\me\AppData\Local\Packages\App_8wekyb3d8bbwe\LocalCache\x", @"Users\me\AppData\Local\Packages", "protected area")]
    [InlineData(@"Users\me\AppData\Local\winup\history.jsonl", @"Users\me\AppData\Local\winup", "protected area")]
    [InlineData(@"Users\me\AppData\Local\D3DSCache\wsl.vhdx", @"Users\me\AppData\Local\D3DSCache", "virtual disk")]
    [InlineData(@"Windows\System32\drivers", @"Windows\System32", "not in an allowed place")]
    public void Refuses(string rel, string scopeRel, string reason)
    {
        using var f = new Fixture();
        var path = f.P(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path) && !Directory.Exists(path)) File.WriteAllText(path, "x");
        var why = new SafetyPolicy(f.Env).WhyRefused(path, f.P(scopeRel), CleanKind.Contents);
        Assert.Equal(reason, why);
    }

    [Fact]
    public void Allows_a_cache_file_inside_a_browser_profile()
    {
        using var f = new Fixture();
        var p = f.P(@"Users\me\AppData\Local\Google\Chrome\User Data\Default\Cache\Cache_Data");
        Assert.Null(new SafetyPolicy(f.Env).WhyRefused(p, Path.GetDirectoryName(p)!, CleanKind.Contents));
    }

    [Fact]
    public void Refuses_the_folder_itself_for_contents_rules_and_paths_outside_the_rule()
    {
        using var f = new Fixture();
        var pol = new SafetyPolicy(f.Env);
        var temp = f.Env[PathEnvironment.Temp];
        Assert.Equal("outside the rule's folder", pol.WhyRefused(temp, temp, CleanKind.Contents));
        Assert.Equal("outside the rule's folder", pol.WhyRefused(f.P(@"Users\me\AppData\Local\CrashDumps\app.dmp"), temp, CleanKind.Contents));
        Assert.Equal("path with ..", pol.WhyRefused(temp + @"\..\..\Documents", temp, CleanKind.Contents));
        Assert.Equal("drive root", pol.WhyRefused(@"C:\", @"C:\", CleanKind.Paths));
        Assert.Equal("network or device path", pol.WhyRefused(@"\\server\share\x", @"\\server\share", CleanKind.Contents));
    }

    [Fact]
    public void Every_rule_points_into_an_allowed_place()
    {
        using var f = new Fixture();
        var pol = new SafetyPolicy(f.Env);
        foreach (var r in RuleSet.LoadDefault().Rules)
            foreach (var pattern in r.Paths)
            {
                var expanded = f.Env.Expand(pattern);
                Assert.True(expanded is not null, $"{r.Id}: unknown token in {pattern}");
                var fixedPart = string.Join('\\', expanded!.Split('\\').TakeWhile(s => s.IndexOfAny(['{', '*', '?']) < 0));
                Assert.True(pol.IsInAllowedPlace(fixedPart), $"{r.Id}: {pattern} is outside SafetyPolicy's allowed places");
            }
    }

    [Fact]
    public void Pattern_expansion_handles_alternatives_and_wildcards_without_entering_links()
    {
        using var f = new Fixture();
        var ud = Path.Combine(f.L, "Google", "Chrome", "User Data");
        var found = FileSystem.Expand(ud + @"\{Default,Profile *}\{Cache,Code Cache}").ToList();
        Assert.Contains(Path.Combine(ud, "Default", "Cache"), found);
        Assert.Contains(Path.Combine(ud, "Profile 1", "Code Cache"), found);
        Fixture.Junction(Path.Combine(ud, "Profile 9"), Path.Combine(f.Elsewhere, "p9"));
        Directory.CreateDirectory(Path.Combine(f.Elsewhere, "p9", "Cache"));
        Assert.DoesNotContain(FileSystem.Expand(ud + @"\Profile *\Cache"), p => p.Contains("Profile 9"));
    }

    [Fact]
    public void Measuring_does_not_count_what_is_behind_a_link()
    {
        using var f = new Fixture();
        f.Pad(@"elsewhere\huge.bin", 50);
        var m = FileSystem.Measure(Path.Combine(f.L, "D3DSCache"));
        Assert.True(m.Bytes < 10L * 1024 * 1024, $"measured {m.Bytes}");
    }
}
