using System.Diagnostics;

namespace Winup.Tests;

/// <summary>
/// A throwaway Windows-like tree in %TEMP%\winup-tests-*. Every test runs against one of these;
/// the real profile is never touched (PathEnvironment.ForFakeRoot points every token inside it).
/// </summary>
public sealed class Fixture : IDisposable
{
    public string Root { get; }
    public PathEnvironment Env { get; }
    public FakeBin Bin { get; }
    public FakeProbe Probe { get; } = new();
    public DateTime Now { get; } = DateTime.UtcNow;
    public string L => Env[PathEnvironment.LocalAppData];
    public string R => Env[PathEnvironment.RoamingAppData];
    public string U => Env[PathEnvironment.UserProfile];
    public string W => Env[PathEnvironment.Windows];
    public string Elsewhere => Path.Combine(Root, "elsewhere");

    public Fixture(bool elevated = false)
    {
        Root = Path.Combine(Path.GetTempPath(), "winup-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Root);
        Env = PathEnvironment.ForFakeRoot(Root, elevated);
        Bin = new FakeBin(Path.Combine(Root, "$fakebin"));
        Build();
    }

    public Scanner Scanner() => new(Env, Probe, () => Now);
    public Cleaner Cleaner() => new(Env, Bin, Probe, () => Now);
    public IReadOnlyList<Candidate> Check() => Scanner().Check(RuleSet.LoadDefault());

    /// <summary>Paths that are expected to be deleted, per level (relative to Root).</summary>
    public static readonly string[] SafeDeletes =
    [
        @"Users\me\AppData\Local\Temp\old-installer.tmp",
        @"Users\me\AppData\Local\Temp\old-dir",
        @"Users\me\AppData\Local\D3DSCache\shader1",
        @"Users\me\AppData\Local\NVIDIA\DXCache\nv.bin",
        @"Users\me\AppData\LocalLow\NVIDIA\PerDriverVersion\DXCache\pdv.bin",
        @"Users\me\AppData\Local\CrashDumps\app.dmp",
        @"Users\me\AppData\Local\Microsoft\Windows\Explorer\thumbcache_256.db",
        @"Users\me\AppData\Roaming\Code\Cache\Cache_Data",
        @"Users\me\AppData\Roaming\Code\CachedData\abc",
        @"Users\me\AppData\Roaming\Code\Partitions\p1\GPUCache\data_0",
        @"Users\me\AppData\Roaming\SomeApp\Cache\blob",
    ];

    public static readonly string[] StandardDeletes =
    [
        @"Users\me\AppData\Local\npm-cache\_cacache\content-v2",
        @"Users\me\AppData\Local\pip\Cache\http",
        @"Users\me\.gradle\caches\modules-2",
    ];

    public static readonly string[] AggressiveDeletes =
    [
        @"Users\me\AppData\Local\Google\Chrome\User Data\Default\Cache\Cache_Data",
        @"Users\me\AppData\Local\Google\Chrome\User Data\Profile 1\Code Cache\js",
        @"Users\me\AppData\Local\Google\Chrome\User Data\ShaderCache\s.bin",
        @"Users\me\AppData\Local\Mozilla\Firefox\Profiles\abc.default\cache2\entries",
    ];

    /// <summary>Must survive every level.</summary>
    public static readonly string[] Precious =
    [
        @"Users\me\AppData\Local\Temp\fresh.tmp",                           // changed in the last 24 h
        @"Users\me\AppData\Roaming\Code\User\settings.json",                // settings
        @"Users\me\AppData\Roaming\Code\Backups\unsaved.txt",               // unsaved files
        @"Users\me\AppData\Local\Google\Chrome\User Data\Default\Login Data",
        @"Users\me\AppData\Local\Google\Chrome\User Data\Default\Cookies",
        @"Users\me\AppData\Local\Google\Chrome\User Data\Default\History",
        @"Users\me\AppData\Local\Google\Chrome\User Data\Local State",
        @"Users\me\AppData\Local\D3DSCache\wsl.vhdx",                        // virtual disk inside a cache
        @"Users\me\AppData\Local\D3DSCache\vm\disk.vhdx",                    // ... and inside a folder there
        @"Users\me\.nuget\packages\newtonsoft.json\13.0.1\lib.dll",          // manual: never by a level
        @"Users\me\.m2\repository\org\a.jar",                                // manual
        @"Users\me\Documents\thesis.docx",
        @"Users\me\.ssh\id_ed25519",
        @"Windows\Temp\old-sys.tmp",                                         // needs admin
        @"elsewhere\precious.txt",                                           // behind a junction
    ];

    private void Build()
    {
        foreach (var p in SafeDeletes.Concat(StandardDeletes).Concat(AggressiveDeletes).Concat(Precious))
            Make(p, p.EndsWith("dir") || p.EndsWith("Cache_Data") || p.EndsWith("content-v2") || p.EndsWith("http") || p.EndsWith("modules-2") || p.EndsWith(@"\js") || p.EndsWith(@"\abc") ? 3 : 0);
        // a junction inside a cache folder pointing outside: neither followed nor removed
        Junction(Path.Combine(L, "D3DSCache", "link-out"), Elsewhere);
        // Chromium layout for SomeApp (found by layout, not by a table rule)
        Make(@"Users\me\AppData\Roaming\SomeApp\Local State", 0);
        // a bit of size so the 1–5 MB minimums are met
        Pad(@"Users\me\AppData\Roaming\SomeApp\Cache\blob", 6);
        Pad(@"Users\me\AppData\Local\Temp\old-installer.tmp", 2);
        Pad(@"Users\me\AppData\Local\D3DSCache\shader1", 2);
        Pad(@"Users\me\AppData\Local\NVIDIA\DXCache\nv.bin", 2);
        Pad(@"Users\me\AppData\Local\CrashDumps\app.dmp", 2);
        Pad(@"Users\me\AppData\Local\Microsoft\Windows\Explorer\thumbcache_256.db", 2);
        Pad(@"Users\me\AppData\Roaming\Code\CachedData\abc\f0", 6);
        Pad(@"Users\me\AppData\Local\npm-cache\_cacache\content-v2\f0", 2);
        Pad(@"Users\me\AppData\Local\pip\Cache\http\f0", 2);
        Pad(@"Users\me\.gradle\caches\modules-2\f0", 2);
        Pad(@"Users\me\AppData\Local\Google\Chrome\User Data\Default\Cache\Cache_Data\f0", 2);
        Pad(@"Users\me\AppData\Local\Mozilla\Firefox\Profiles\abc.default\cache2\entries", 2);
        Pad(@"Users\me\.nuget\packages\newtonsoft.json\13.0.1\lib.dll", 2);
        Pad(@"Users\me\.m2\repository\org\a.jar", 2);
        Pad(@"Windows\Temp\old-sys.tmp", 2);
        // everything is old, except what is meant to be fresh
        Age(Root, Now.AddDays(-10));
        File.SetLastWriteTimeUtc(Path.Combine(Root, @"Users\me\AppData\Local\Temp\fresh.tmp"), Now.AddHours(-1));
    }

    public string P(string rel) => Path.Combine(Root, rel);

    public void Make(string rel, int files)
    {
        var full = P(rel);
        if (files > 0)
        {
            Directory.CreateDirectory(full);
            for (int i = 0; i < files; i++) File.WriteAllText(Path.Combine(full, $"f{i}"), "x");
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "x");
        }
    }

    public void Pad(string rel, int mb)
    {
        var full = P(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var fs = new FileStream(full, FileMode.Create);
        fs.SetLength(mb * 1024L * 1024);
    }

    public static void Junction(string link, string target)
    {
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (!Directory.Exists(link)) throw new InvalidOperationException("mklink /J failed");
    }

    private static void Age(string dir, DateTime t)
    {
        foreach (var f in Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            File.SetLastWriteTimeUtc(f, t);
        foreach (var d in Directory.EnumerateDirectories(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }).Reverse())
            Directory.SetLastWriteTimeUtc(d, t);
    }

    /// <summary>Every file and link under Root, relative (the fake bin excluded).</summary>
    public SortedSet<string> Snapshot()
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(string d)
        {
            foreach (var e in new DirectoryInfo(d).EnumerateFileSystemInfos())
            {
                var rel = Path.GetRelativePath(Root, e.FullName);
                if (rel.StartsWith("$fakebin")) continue;
                bool link = (e.Attributes & FileAttributes.ReparsePoint) != 0;
                if (e is DirectoryInfo && !link) { Walk(e.FullName); if (!Directory.EnumerateFileSystemEntries(e.FullName).Any()) set.Add(rel + "\\"); }
                else set.Add(rel + (link ? " -> link" : ""));
            }
        }
        Walk(Root);
        return set;
    }

    public void Dispose()
    {
        try
        {
            // remove junctions first so deleting the tree never walks through them
            foreach (var d in Directory.EnumerateDirectories(Root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }).ToList())
                if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) Directory.Delete(d);
            Directory.Delete(Root, true);
        }
        catch { }
    }
}

/// <summary>Moves "deleted" things into a folder inside the fixture, so tests can see exactly what went.</summary>
public sealed class FakeBin(string dir) : IRecycler
{
    public List<string> Recycled { get; } = [];

    public string? Recycle(string path)
    {
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, Recycled.Count + "_" + Path.GetFileName(path));
        try
        {
            if (Directory.Exists(path)) Directory.Move(path, dest);
            else File.Move(path, dest);
        }
        catch (Exception e) { return e.Message; }
        Recycled.Add(path);
        return null;
    }
}

/// <summary>Pretends these process names are running.</summary>
public sealed class FakeProbe : IAppProbe
{
    public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> OpenApps(Candidate c) => c.Rule.Processes.Where(Running.Contains).ToList();
}
