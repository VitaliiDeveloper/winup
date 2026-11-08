namespace Winup.Core;

/// <summary>Something that can be cleaned, found by a check. A check never changes anything.</summary>
public sealed class Candidate
{
    public required Rule Rule { get; init; }
    /// <summary>The folders (contents) or files/folders (paths) the rule matched on this PC.</summary>
    public required IReadOnlyList<string> Targets { get; init; }
    public required Measurement Size { get; init; }
    /// <summary>The rule needs administrator rights and winup runs without them.</summary>
    public bool NeedsAdmin { get; init; }
    /// <summary>Apps that are open and use these files; the candidate is skipped while they run.</summary>
    public IReadOnlyList<string> OpenApps { get; init; } = [];

    public string Id => Rule.Id;
    public Level Level => Rule.Level;
    public long Bytes => Size.Bytes;
    public bool Selectable => !NeedsAdmin && OpenApps.Count == 0 && Size.Bytes > 0;
    /// <summary>Chosen by "clean up to level X" (not manual-only, not blocked).</summary>
    public bool InLevel(Level level) => Selectable && !Rule.Manual && Rule.Level <= level;
}

public sealed class Scanner
{
    private readonly PathEnvironment _env;
    private readonly IAppProbe _probe;
    private readonly Func<DateTime> _now;

    public Scanner(PathEnvironment env, IAppProbe probe, Func<DateTime>? now = null)
    {
        _env = env;
        _probe = probe;
        _now = now ?? (() => DateTime.UtcNow);
        _policy = new SafetyPolicy(env);
    }

    private readonly SafetyPolicy _policy;

    /// <summary>Lists what can be cleaned, biggest first within each level. Reads only.</summary>
    public IReadOnlyList<Candidate> Check(RuleSet rules, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var all = rules.Rules.Concat(ElectronDiscovery.Find(_env, rules)).ToList();
        var found = new System.Collections.Concurrent.ConcurrentBag<Candidate>();
        Parallel.ForEach(all, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8), CancellationToken = ct }, rule =>
        {
            progress?.Report(rule.LocalizedLabel);
            var c = Inspect(rule);
            if (c is not null) found.Add(c);
        });
        // a path claimed by two rules is shown once, under the first (static) rule
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = all.Select((r, i) => (r.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var result = new List<Candidate>();
        foreach (var c in found.OrderBy(c => order[c.Id]))
        {
            if (c.Targets.All(claimed.Contains)) continue;
            foreach (var t in c.Targets) claimed.Add(t);
            result.Add(c);
        }
        return result.OrderBy(c => c.Level).ThenByDescending(c => c.Bytes).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>One rule on this PC, or null if it has nothing here.</summary>
    public Candidate? Inspect(Rule rule)
    {
        var targets = new List<string>();
        foreach (var pattern in rule.Paths)
        {
            var expanded = _env.Expand(pattern);
            if (expanded is null) continue;
            foreach (var p in FileSystem.Expand(expanded))
            {
                if (_policy.HasLinkOnTheWay(p)) continue;
                if (rule.Kind == CleanKind.Contents && !Directory.Exists(p)) continue;
                if (!targets.Contains(p, StringComparer.OrdinalIgnoreCase)) targets.Add(p);
            }
        }
        if (targets.Count == 0) return null;

        var size = Measurement.Empty;
        foreach (var t in targets) size = size.Add(MeasureTarget(rule, t));
        bool needsAdmin = rule.Admin && !_env.IsElevated;
        if (size.Bytes < rule.MinBytes && !(needsAdmin && size.Partial)) return null;

        var c = new Candidate { Rule = rule, Targets = targets, Size = size, NeedsAdmin = needsAdmin };
        var open = _probe.OpenApps(c);
        return open.Count == 0 ? c : new Candidate { Rule = rule, Targets = targets, Size = size, NeedsAdmin = needsAdmin, OpenApps = open };
    }

    /// <summary>What a cleanup of this target would remove: for contents, the children old enough, links excluded.</summary>
    public Measurement MeasureTarget(Rule rule, string target)
    {
        if (rule.Kind == CleanKind.Paths) return FileSystem.Measure(target);
        var total = Measurement.Empty;
        var cutoff = _now() - TimeSpan.FromHours(rule.MinAgeHours);
        bool canList = true;
        try { _ = Directory.EnumerateFileSystemEntries(target).FirstOrDefault(); }
        catch { canList = false; }
        if (!canList) return Measurement.Empty with { Partial = true };
        foreach (var child in FileSystem.Children(target))
        {
            if (FileSystem.IsLink(child)) continue;
            var m = FileSystem.Measure(child.FullName);
            if (rule.MinAgeHours > 0 && Newest(child, m) > cutoff) continue;
            total = total.Add(m);
        }
        return total;
    }

    internal static DateTime Newest(FileSystemInfo fi, Measurement m)
    {
        var own = fi.LastWriteTimeUtc;
        return m.NewestUtc > own ? m.NewestUtc : own;
    }
}

/// <summary>
/// Chromium and Electron apps winup has no rule for, found by their folder layout (a "Local State" or
/// "Preferences" file next to cache folders). A browser (History next to it) is aggressive, an app safe.
/// </summary>
public static class ElectronDiscovery
{
    public static IEnumerable<Rule> Find(PathEnvironment env, RuleSet rules)
    {
        var cacheDirs = rules.ElectronCacheDirs;
        if (cacheDirs.Count == 0) yield break;
        var group = "{" + string.Join(',', cacheDirs) + "}";
        var policy = new SafetyPolicy(env);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // folders a table rule already owns (Chrome's User Data, VS Code's folder, %TEMP%, …)
        var owned = rules.Rules.SelectMany(r => r.Paths).Select(p => FixedPrefix(env, p)).OfType<string>().ToList();
        foreach (var token in new[] { PathEnvironment.RoamingAppData, PathEnvironment.LocalAppData })
        {
            var root = env[token];
            if (!Directory.Exists(root)) continue;
            foreach (var dir in ChromiumDirs(root, 0))
            {
                if (!policy.IsInAllowedPlace(dir) || !seen.Add(dir)) continue;
                if (owned.Any(o => Paths.IsSameOrUnder(o, dir) || Paths.IsSameOrUnder(dir, o))) continue;
                var rel = dir[(root.Length + 1)..].Split('\\');
                var name = AppName(rel, rules.AppNames);
                // a browser keeps bookmarks; apps that embed Chromium/WebView2 have History too, but no Bookmarks
                bool browser = File.Exists(Path.Combine(dir, "Bookmarks")) || File.Exists(Path.Combine(dir, "Default", "Bookmarks"));
                var slug = new string(dir[(root.Length + 1)..].ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
                yield return new Rule
                {
                    Id = $"apps.found.{token.ToLowerInvariant()}.{slug}",
                    Level = browser ? Level.Aggressive : Level.Safe,
                    Category = browser ? "browsers" : "apps",
                    Label = browser ? "{0} cache" : "{0} caches",
                    LabelArg = name,
                    Note = browser
                        ? "Sites load a little slower at first. Passwords, history, bookmarks, cookies and tabs are not touched."
                        : "Cache of an app built on Chromium. Close the app first; sign-in, settings and unsaved files are not touched.",
                    Kind = CleanKind.Contents,
                    Paths = [$@"{dir}\{group}", $@"{dir}\{{Default,Partitions\*}}\{group}"],
                    // the app's exe is usually named like its folder (NVIDIA App, olk, Spark Desktop)
                    Processes = rel.Where(s => !GenericNames.Contains(s)).Append(name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    ProbeOpenFiles = true,
                    MinBytes = 5 * 1024 * 1024,
                };
            }
        }
    }

    /// <summary>The part of a pattern before its first wildcard or alternative, expanded; null when it is just a top folder.</summary>
    internal static string? FixedPrefix(PathEnvironment env, string pattern)
    {
        var expanded = env.Expand(pattern);
        if (expanded is null) return null;
        var segs = expanded.Split('\\');
        int stop = Array.FindIndex(segs, s => s.IndexOfAny(['{', '*', '?']) >= 0);
        var prefix = stop < 0 ? expanded : string.Join('\\', segs, 0, stop);
        prefix = Paths.Normalize(prefix);
        bool isTop = env.Tokens.Values.Any(v => v.Equals(prefix, StringComparison.OrdinalIgnoreCase)) &&
                     !prefix.Equals(env[PathEnvironment.Temp], StringComparison.OrdinalIgnoreCase);
        return isTop ? null : prefix;
    }

    private static bool LooksChromium(string dir) =>
        File.Exists(Path.Combine(dir, "Local State")) ||
        (File.Exists(Path.Combine(dir, "Preferences")) &&
         new[] { "Cookies", "Network", "TransportSecurity", "Network Persistent State", "DIPS" }.Any(n => Path.Exists(Path.Combine(dir, n))));

    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Programs", "User Data", "EBWebView", "CefCache", "WebView2", "WebView", "Default", "htmlcache", "Local", "Roaming",
    };

    /// <summary>A readable name from the folder path: the last part that is not generic or a Store package id.</summary>
    internal static string AppName(string[] rel, IReadOnlyDictionary<string, string> known)
    {
        string? pick = null;
        foreach (var s in rel)
        {
            if (GenericNames.Contains(s) || System.Text.RegularExpressions.Regex.IsMatch(s, "_[a-z0-9]{13}$")) continue;
            pick = s;
        }
        pick ??= rel[0];
        if (pick.EndsWith("-Data", StringComparison.OrdinalIgnoreCase) || pick.EndsWith(" Data", StringComparison.OrdinalIgnoreCase)) pick = pick[..^5];
        return known.TryGetValue(pick, out var friendly) ? friendly : pick;
    }

    private static readonly HashSet<string> SkipTop =new(StringComparer.OrdinalIgnoreCase) { "Packages", "Temp", "winup", "wsl", "Docker", "CrashDumps" };

    private static IEnumerable<string> ChromiumDirs(string dir, int depth)
    {
        foreach (var child in FileSystem.Children(dir))
        {
            if ((child.Attributes & FileAttributes.Directory) == 0 || FileSystem.IsLink(child)) continue;
            if (depth == 0 && SkipTop.Contains(child.Name)) continue;
            if (SafetyPolicy.CacheFolderNames.Contains(child.Name)) continue;
            if (LooksChromium(child.FullName)) { yield return child.FullName; continue; }
            if (depth < 3)
                foreach (var d in ChromiumDirs(child.FullName, depth + 1)) yield return d;
        }
    }
}
