using System.Runtime.InteropServices;
using System.Text;
using Winup.Core;

namespace Winup.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        bool color = !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null && Ansi.Enable();
        var app = new CommandLine(PathEnvironment.Current(), new RecycleBin(), new WindowsAppProbe(), Console.Out, Console.In,
            interactive: !Console.IsInputRedirected && !Console.IsOutputRedirected, color: color);
        return app.Run(args);
    }
}

/// <summary>The command line, with everything it touches passed in so tests can run it on a fake tree.</summary>
public sealed class CommandLine(PathEnvironment env, IRecycler recycler, IAppProbe probe, TextWriter o, TextReader i, bool interactive, bool color)
{
    private bool _color = color;
    private string G => _color ? "\e[32m" : "";
    private string Y => _color ? "\e[33m" : "";
    private string R => _color ? "\e[31m" : "";
    private string D => _color ? "\e[2m" : "";
    private string B => _color ? "\e[1m" : "";
    private string N => _color ? "\e[0m" : "";

    public int Run(string[] args)
    {
        string? command = null;
        var level = Level.Safe;
        bool yes = false, pickAllowed = true;
        for (int k = 0; k < args.Length; k++)
        {
            var a = args[k];
            switch (a)
            {
                case "--lang":
                    if (++k >= args.Length || !Loc.Languages.Contains(args[k])) return Usage("--lang en|uk|ru");
                    Loc.Lang = args[k];
                    break;
                case "--level":
                    if (++k >= args.Length || !Levels.TryParse(args[k], out level)) return Usage("--level safe|standard|aggressive");
                    break;
                case "--yes" or "-y": yes = true; break;
                case "--no-color": _color = false; break;
                case "--no-pick": pickAllowed = false; break;
                case "--version" or "-V": o.WriteLine("winup " + Version); return 0;
                case "-h" or "--help": command = "help"; break;
                default:
                    if (a.StartsWith('-')) return Usage(Loc.F("unknown option {0}", a));
                    if (command is not null) return Usage(Loc.F("unexpected argument {0}", a));
                    command = a;
                    break;
            }
        }
        switch (command)
        {
            case null: Help(); return 2;
            case "help": Help(); return 0;
            case "check": return Check(interactive && pickAllowed);
            case "clean": return Clean(level, yes);
            case "history": return History();
            case "rules": return RulesList();
            default:
                o.WriteLine(Loc.F("Unknown command: {0}", command));
                Help();
                return 1;
        }
    }

    private static string Version => typeof(CommandLine).Assembly.GetName().Version?.ToString(3) ?? "0";

    private int Usage(string msg)
    {
        o.WriteLine($"{R}winup: {msg}{N}");
        return 2;
    }

    private void Help()
    {
        o.WriteLine($"{B}winup{N} {Version} — {Loc.T("keep your Windows PC clean")}");
        o.WriteLine();
        o.WriteLine($"  winup check          {Loc.T("list what can be cleaned (changes nothing), then pick")}");
        o.WriteLine($"  winup clean          {Loc.T("clean everything up to a level (asks first)")}");
        o.WriteLine($"  winup history        {Loc.T("past cleanups")}");
        o.WriteLine($"  winup rules          {Loc.T("what winup knows how to clean")}");
        o.WriteLine();
        o.WriteLine($"  --level safe|standard|aggressive   {Loc.T("default: safe")}");
        o.WriteLine("  --lang en|uk|ru   --yes   --no-color   --no-pick   --version");
        o.WriteLine();
        o.WriteLine(D + Loc.T("Everything goes to the Recycle Bin. Open apps are skipped. Links and junctions are never followed.") + N);
    }

    private IReadOnlyList<Candidate> Scan()
    {
        o.WriteLine(D + Loc.T("Checking… (nothing is changed)") + N);
        return new Scanner(env, probe).Check(RuleSet.LoadDefault());
    }

    private string LevelCol(Level l) => l switch { Level.Safe => G, Level.Standard => Y, _ => R };

    private static string Pad(string s, int w) => s.Length >= w ? s : s + new string(' ', w - s.Length);

    private List<Candidate> List(IReadOnlyList<Candidate> all)
    {
        var shown = all.Where(c => c.Bytes > 0 || c.NeedsAdmin).ToList();
        if (shown.Count == 0)
        {
            o.WriteLine(Loc.T("Nothing to clean."));
            return shown;
        }
        o.WriteLine();
        o.WriteLine($"{B}{Loc.T("What can be cleaned")}{N}");
        int n = 0;
        foreach (var c in shown)
        {
            n++;
            var size = c.Bytes > 0 ? Format.Bytes(c.Bytes) : "—";
            var tags = new List<string>();
            if (c.NeedsAdmin) tags.Add(Loc.T("needs administrator rights"));
            if (c.OpenApps.Count > 0) tags.Add(Loc.F("skipped while open: {0}", string.Join(", ", c.OpenApps)));
            if (c.Rule.Manual) tags.Add(Loc.T("only if you pick it"));
            var tag = tags.Count > 0 ? $" {D}({string.Join("; ", tags)}){N}" : "";
            o.WriteLine($"  {n,3}  {LevelCol(c.Level)}{Pad(c.Level.Word(), 12)}{N}{size,10}  {c.Rule.LocalizedLabel}{tag}");
        }
        o.WriteLine();
        long Sum(Level l) => all.Where(c => c.InLevel(l)).Sum(c => c.Bytes);
        o.WriteLine(Loc.F("Up to level: {0} {1} · {2} {3} · {4} {5}",
            Level.Safe.Word(), Format.Bytes(Sum(Level.Safe)), Level.Standard.Word(), Format.Bytes(Sum(Level.Standard)),
            Level.Aggressive.Word(), Format.Bytes(Sum(Level.Aggressive))));
        return shown;
    }

    private int Check(bool pick)
    {
        var shown = List(Scan());
        if (!pick || shown.Count == 0) return 0;
        o.WriteLine();
        o.Write(Loc.T("Clean now? Numbers (1,3 or 2-5), a level (safe, standard, aggressive), or Enter to skip: "));
        var chosen = Pick(i.ReadLine() ?? "", shown);
        if (chosen.Count == 0) return 0;
        return Confirm(chosen, ask: true);
    }

    /// <summary>"1,3 5-7 standard" → those candidates (numbers as listed; a level adds what it would clean).</summary>
    public static List<Candidate> Pick(string answer, IReadOnlyList<Candidate> shown)
    {
        var set = new SortedSet<int>();
        foreach (var tok in answer.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var lv = LevelFromWord(tok);
            if (lv is not null)
            {
                for (int k = 0; k < shown.Count; k++) if (shown[k].InLevel(lv.Value)) set.Add(k);
                continue;
            }
            var parts = tok.Split('-');
            if (parts.Length is 1 or 2 && int.TryParse(parts[0], out int a) && int.TryParse(parts[^1], out int b))
                for (int k = Math.Max(1, a); k <= Math.Min(b, shown.Count); k++) set.Add(k - 1);
        }
        return set.Select(k => shown[k]).ToList();
    }

    private static Level? LevelFromWord(string tok)
    {
        if (!int.TryParse(tok, out _) && Levels.TryParse(tok, out var l)) return l;
        foreach (var x in new[] { Level.Safe, Level.Standard, Level.Aggressive })
            if (tok.Equals(x.Word(), StringComparison.CurrentCultureIgnoreCase)) return x;
        return null;
    }

    private int Clean(Level level, bool yes)
    {
        var all = Scan();
        List(all);
        var chosen = all.Where(c => c.InLevel(level)).ToList();
        if (chosen.Count == 0)
        {
            o.WriteLine(Loc.F("Nothing to clean at level {0}.", level.Word()));
            return 0;
        }
        if (!yes && !interactive)
        {
            o.WriteLine(Loc.T("Add --yes to clean without a question."));
            return 2;
        }
        return Confirm(chosen, ask: !yes);
    }

    private int Confirm(List<Candidate> chosen, bool ask)
    {
        foreach (var c in chosen.Where(c => !c.Selectable))
            o.WriteLine($"{D}{Loc.F("Skipped: {0}", c.Rule.LocalizedLabel)}{N}");
        chosen = chosen.Where(c => c.Selectable).ToList();
        if (chosen.Count == 0) return 0;
        long total = chosen.Sum(c => c.Bytes);
        if (ask)
        {
            o.Write(Loc.F("Move {0} items ({1}) to the Recycle Bin? [y/N] ", chosen.Count, Format.Bytes(total)));
            var a = (i.ReadLine() ?? "").Trim().ToLowerInvariant();
            if (a is not ("y" or "yes" or "т" or "так" or "д" or "да"))
            {
                o.WriteLine(Loc.T("Nothing was changed."));
                return 0;
            }
        }
        var result = new Cleaner(env, recycler, probe).Clean(chosen);
        o.WriteLine();
        foreach (var item in result.Items)
        {
            if (item.SkippedBecause is not null)
            {
                o.WriteLine($"  {Y}–{N} {item.Label}: {Loc.T("skipped")} ({item.SkippedBecause})");
                continue;
            }
            var fail = item.Failed > 0 ? $" {D}({Loc.F("{0} in use or refused", item.Failed)}){N}" : "";
            o.WriteLine($"  {G}✓{N} {item.Label}: {Format.Bytes(item.FreedBytes)}{fail}");
        }
        var entry = new HistoryStore(env).Save(result);
        o.WriteLine();
        o.WriteLine(B + Loc.F("Moved to the Recycle Bin: {0}. The space is freed when you empty the Recycle Bin.", Format.Bytes(result.FreedBytes)) + N);
        o.WriteLine(D + Loc.F("Report: {0}", entry.Report) + N);
        return 0;
    }

    private int History()
    {
        var list = new HistoryStore(env).Load();
        if (list.Count == 0)
        {
            o.WriteLine(Loc.T("No cleanups yet."));
            return 0;
        }
        foreach (var e in list.TakeLast(30))
            o.WriteLine($"  {e.Time:yyyy-MM-dd HH:mm}  {Format.Bytes(e.FreedBytes),10}  {Loc.T("free")}: {Format.Bytes(e.FreeSpaceBytes)}");
        return 0;
    }

    private int RulesList()
    {
        foreach (var r in RuleSet.LoadDefault().Rules.OrderBy(r => r.Level).ThenBy(r => r.Category).ThenBy(r => r.Id))
        {
            var extra = (r.Admin ? " [admin]" : "") + (r.Manual ? " [manual]" : "");
            o.WriteLine($"  {LevelCol(r.Level)}{Pad(r.Level.Word(), 12)}{N}{Pad(r.Id, 34)} {r.LocalizedLabel}{D}{extra}{N}");
        }
        return 0;
    }
}

internal static class Ansi
{
    public static bool Enable()
    {
        try
        {
            var h = GetStdHandle(-11);
            if (!GetConsoleMode(h, out uint mode)) return false;
            return SetConsoleMode(h, mode | 0x0004 /* ENABLE_VIRTUAL_TERMINAL_PROCESSING */);
        }
        catch { return false; }
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int n);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr h, out uint mode);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr h, uint mode);
}
