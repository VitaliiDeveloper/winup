using System.Text;
using System.Text.Json;

namespace Winup.Core;

public sealed record HistoryEntry(DateTime Time, long FreedBytes, int Removed, int Failed, int Skipped, long FreeSpaceBytes, string? Report);

/// <summary>Every cleanup leaves a Markdown report and a line in history.jsonl under %LOCALAPPDATA%\winup.</summary>
public sealed class HistoryStore
{
    private readonly PathEnvironment _env;

    public HistoryStore(PathEnvironment env) => _env = env;

    public string ReportsDir => Path.Combine(_env.DataDir, "reports");
    public string HistoryFile => Path.Combine(_env.DataDir, "history.jsonl");

    public HistoryEntry Save(CleanResult result)
    {
        Directory.CreateDirectory(ReportsDir);
        var local = result.StartedUtc.ToLocalTime();
        var report = Path.Combine(ReportsDir, $"winup_{local:yyyy-MM-dd_HHmmss}.md");
        File.WriteAllText(report, Markdown(result), new UTF8Encoding(false));
        long free = 0;
        try { free = new DriveInfo(Path.GetPathRoot(_env[PathEnvironment.LocalAppData])!).AvailableFreeSpace; } catch { }
        var entry = new HistoryEntry(local, result.FreedBytes, result.Removed, result.Failed, result.Skipped.Count(), free, report);
        File.AppendAllText(HistoryFile, JsonSerializer.Serialize(entry) + Environment.NewLine, new UTF8Encoding(false));
        return entry;
    }

    public IReadOnlyList<HistoryEntry> Load()
    {
        if (!File.Exists(HistoryFile)) return [];
        var list = new List<HistoryEntry>();
        foreach (var line in File.ReadLines(HistoryFile))
        {
            try { if (JsonSerializer.Deserialize<HistoryEntry>(line) is { } e) list.Add(e); }
            catch { }
        }
        return list;
    }

    public static string Markdown(CleanResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# winup — {r.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine(r.ToRecycleBin
            ? Loc.F("Moved to the Recycle Bin: {0}. The space is freed when you empty the Recycle Bin.", Format.Bytes(r.FreedBytes))
            : Loc.F("Removed: {0}", Format.Bytes(r.FreedBytes)));
        sb.AppendLine();
        foreach (var i in r.Items)
        {
            var state = i.SkippedBecause is not null ? Loc.T("skipped") + ": " + i.SkippedBecause : Format.Bytes(i.FreedBytes);
            sb.AppendLine($"- [{i.Level.Word()}] {i.Label}: {state}");
            foreach (var p in i.Problems.Take(20)) sb.AppendLine($"  - {p}");
            if (i.Problems.Count > 20) sb.AppendLine($"  - … +{i.Problems.Count - 20}");
        }
        return sb.ToString();
    }
}
