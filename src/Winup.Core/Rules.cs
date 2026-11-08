using System.Text.Json;

namespace Winup.Core;

/// <summary>One line of the cleanup table (Data/rules.json).</summary>
public sealed record Rule
{
    public required string Id { get; init; }
    public required Level Level { get; init; }
    public required string Category { get; init; }
    /// <summary>English label; also the key into strings.json. May contain {0} for <see cref="LabelArg"/>.</summary>
    public required string Label { get; init; }
    public string? LabelArg { get; init; }
    public string? Note { get; init; }
    public CleanKind Kind { get; init; } = CleanKind.Contents;
    /// <summary>Patterns with %TOKEN%, {a,b} and * (see <see cref="FileSystem.Expand"/>).</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];
    /// <summary>Process names (without .exe). While one runs, the rule is skipped.</summary>
    public IReadOnlyList<string> Processes { get; init; } = [];
    /// <summary>Needs administrator rights to delete.</summary>
    public bool Admin { get; init; }
    /// <summary>Never chosen by a level; only when you tick it yourself.</summary>
    public bool Manual { get; init; }
    /// <summary>Smaller than this: not listed.</summary>
    public long MinBytes { get; init; } = 1024 * 1024;
    /// <summary>Contents only: entries changed more recently than this stay (a running installer's temp files).</summary>
    public double MinAgeHours { get; init; }
    /// <summary>Ask Windows (Restart Manager) which programs hold files here open; used for apps found by folder layout.</summary>
    public bool ProbeOpenFiles { get; init; }

    public string LocalizedLabel => LabelArg is null ? Loc.T(Label) : Loc.F(Label, LabelArg);
    public string? LocalizedNote => Note is null ? null : Loc.T(Note);
}

public sealed class RuleSet
{
    public required IReadOnlyList<Rule> Rules { get; init; }
    /// <summary>Cache folder names of Chromium/Electron apps (also used to discover unknown apps).</summary>
    public required IReadOnlyList<string> ElectronCacheDirs { get; init; }
    /// <summary>Folder name → app name for apps found by layout (Olk → Outlook (new)).</summary>
    public IReadOnlyDictionary<string, string> AppNames { get; init; } = new Dictionary<string, string>();

    public static RuleSet LoadDefault()
    {
        using var s = typeof(RuleSet).Assembly.GetManifestResourceStream("Winup.Core.rules.json")
            ?? throw new InvalidOperationException("rules.json is not embedded");
        using var r = new StreamReader(s);
        return Parse(r.ReadToEnd());
    }

    public static RuleSet Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<RulesFile>(json, JsonOpts) ?? throw new InvalidDataException("empty rules file");
        var rules = new List<Rule>();
        foreach (var d in dto.Rules)
        {
            if (!Levels.TryParse(d.Level, out var level)) throw new InvalidDataException($"rule {d.Id}: bad level '{d.Level}'");
            rules.Add(new Rule
            {
                Id = d.Id ?? throw new InvalidDataException("rule without id"),
                Level = level,
                Category = d.Category ?? "other",
                Label = d.Label ?? d.Id,
                Note = d.Note,
                Kind = d.Kind?.ToLowerInvariant() == "paths" ? CleanKind.Paths : CleanKind.Contents,
                Paths = d.Paths ?? [],
                Processes = d.Processes ?? [],
                Admin = d.Admin,
                Manual = d.Manual,
                MinBytes = d.MinBytes ?? 1024 * 1024,
                MinAgeHours = d.MinAgeHours ?? 0,
            });
        }
        var cacheDirs = dto.ElectronCacheDirs ?? [];
        string group = "{" + string.Join(',', cacheDirs) + "}";
        foreach (var app in dto.ElectronApps ?? [])
        {
            var slug = new string(app.Name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            rules.Add(new Rule
            {
                Id = "apps.electron." + slug,
                Level = Level.Safe,
                Category = "apps",
                Label = "{0} caches",
                LabelArg = app.Name,
                Note = "Cache of an Electron app. Close the app first; sign-in, settings and unsaved files are not touched.",
                Kind = CleanKind.Contents,
                Paths = [$@"{app.Folder}\{group}", $@"{app.Folder}\{{Default,Partitions\*}}\{group}"],
                Processes = app.Processes ?? [],
                ProbeOpenFiles = true,
                MinBytes = 5 * 1024 * 1024,
            });
        }
        var dup = rules.GroupBy(r => r.Id).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new InvalidDataException($"duplicate rule id {dup.Key}");
        return new RuleSet
        {
            Rules = rules,
            ElectronCacheDirs = cacheDirs,
            AppNames = new Dictionary<string, string>(dto.AppNames ?? [], StringComparer.OrdinalIgnoreCase),
        };
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class RulesFile
    {
        public int Version { get; set; }
        public List<string>? ElectronCacheDirs { get; set; }
        public List<ElectronApp>? ElectronApps { get; set; }
        public Dictionary<string, string>? AppNames { get; set; }
        public List<RuleDto> Rules { get; set; } = [];
    }

    private sealed class ElectronApp
    {
        public string Folder { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string>? Processes { get; set; }
    }

    private sealed class RuleDto
    {
        public string? Id { get; set; }
        public string? Level { get; set; }
        public string? Category { get; set; }
        public string? Label { get; set; }
        public string? Note { get; set; }
        public string? Kind { get; set; }
        public List<string>? Paths { get; set; }
        public List<string>? Processes { get; set; }
        public bool Admin { get; set; }
        public bool Manual { get; set; }
        public long? MinBytes { get; set; }
        public double? MinAgeHours { get; set; }
    }
}
