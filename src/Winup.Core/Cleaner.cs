namespace Winup.Core;

public sealed record CleanedItem(string Id, string Label, Level Level, long FreedBytes, int Removed, int Failed, string? SkippedBecause, IReadOnlyList<string> Problems);

public sealed record CleanResult(DateTime StartedUtc, TimeSpan Duration, bool ToRecycleBin, IReadOnlyList<CleanedItem> Items)
{
    public long FreedBytes => Items.Sum(i => i.FreedBytes);
    public int Removed => Items.Sum(i => i.Removed);
    public int Failed => Items.Sum(i => i.Failed);
    public IEnumerable<CleanedItem> Skipped => Items.Where(i => i.SkippedBecause is not null);
}

/// <summary>
/// Second phase: cleans the candidates you chose. Right before each delete it asks again whether the app is
/// open and re-checks every single path with <see cref="SafetyPolicy"/>; a link is never followed or removed.
/// </summary>
public sealed class Cleaner
{
    private readonly PathEnvironment _env;
    private readonly SafetyPolicy _policy;
    private readonly IRecycler _recycler;
    private readonly IAppProbe _probe;
    private readonly Scanner _scanner;
    private readonly Func<DateTime> _now;

    public Cleaner(PathEnvironment env, IRecycler recycler, IAppProbe probe, Func<DateTime>? now = null)
    {
        _env = env;
        _policy = new SafetyPolicy(env);
        _recycler = recycler;
        _probe = probe;
        _now = now ?? (() => DateTime.UtcNow);
        _scanner = new Scanner(env, probe, _now);
    }

    public CleanResult Clean(IEnumerable<Candidate> chosen, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var started = _now();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var items = new List<CleanedItem>();
        foreach (var c in chosen)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(c.Rule.LocalizedLabel);
            items.Add(CleanOne(c, ct));
        }
        return new CleanResult(started, watch.Elapsed, _recycler is RecycleBin, items);
    }

    private CleanedItem CleanOne(Candidate c, CancellationToken ct)
    {
        string label = c.Rule.LocalizedLabel;
        if (c.Rule.Admin && !_env.IsElevated)
            return new CleanedItem(c.Id, label, c.Level, 0, 0, 0, "needs administrator rights", []);
        var open = _probe.OpenApps(c);
        if (open.Count > 0)
            return new CleanedItem(c.Id, label, c.Level, 0, 0, 0, "open: " + string.Join(", ", open), []);

        long freed = 0;
        int removed = 0, failed = 0;
        var problems = new List<string>();
        var cutoff = _now() - TimeSpan.FromHours(c.Rule.MinAgeHours);

        void Remove(string path, string scope, Measurement m)
        {
            var why = _policy.WhyRefused(path, scope, c.Rule.Kind);
            if (why is "gone") return;
            if (why is not null) { failed++; problems.Add($"refused ({why}): {path}"); return; }
            var err = _recycler.Recycle(path);
            if (err is null) { removed++; freed += m.Bytes; }
            else { failed++; problems.Add($"{err}: {path}"); }
        }

        foreach (var target in c.Targets)
        {
            ct.ThrowIfCancellationRequested();
            if (c.Rule.Kind == CleanKind.Paths)
            {
                if (!Path.Exists(target)) continue;
                Remove(target, target, FileSystem.Measure(target));
                continue;
            }
            // contents: every child is checked on its own below, including every folder above it up to the allowed place
            if (!Directory.Exists(target) || FileSystem.IsLink(target)) continue;
            foreach (var child in FileSystem.Children(target))
            {
                ct.ThrowIfCancellationRequested();
                if (FileSystem.IsLink(child)) continue; // links are neither followed nor removed
                var m = FileSystem.Measure(child.FullName);
                if (c.Rule.MinAgeHours > 0 && Scanner.Newest(child, m) > cutoff) continue;
                Remove(child.FullName, target, m);
            }
        }
        return new CleanedItem(c.Id, label, c.Level, freed, removed, failed, null, problems);
    }
}
