namespace Winup.Core;

/// <summary>
/// Sample results for trying winup and for the screenshots in the docs (<c>--demo</c>).
/// Nothing is read from the disk, and a demo cleanup changes nothing.
/// </summary>
public static class Demo
{
    public static IReadOnlyList<Candidate> Candidates()
    {
        var rules = RuleSet.LoadDefault().Rules.ToDictionary(r => r.Id);
        var month = DateTime.UtcNow.AddDays(-30);

        Candidate C(string id, double mb, string[]? open = null, bool admin = false) => new()
        {
            Rule = rules[id],
            Targets = [@"C:\demo\" + id],
            Size = new Measurement((long)(mb * 1024 * 1024), 1000, month, admin && mb == 0),
            NeedsAdmin = admin,
            OpenApps = open ?? [],
        };

        var list = new List<Candidate>
        {
            C("gpu.directx", 1843),
            C("gpu.nvidia", 1126),
            C("temp.user", 912),
            C("apps.electron.visualstudiocode", 684),
            C("apps.electron.slack", 422),
            C("apps.electron.discord", 312, open: ["Discord"]),
            C("dumps.wer_user", 268),
            C("temp.thumbnails", 96),
            C("temp.windows", 0, admin: true),
            C("dev.npm", 2150),
            C("dev.gradle", 1730),
            C("dev.nuget_http", 640),
            C("dev.pip", 380),
            C("browser.chrome", 1240),
            C("browser.edge", 515, open: ["Microsoft Edge"]),
            C("dev.nuget_packages", 3100),
        };
        return list.OrderBy(c => c.Level).ThenByDescending(c => c.Bytes).ToList();
    }

    /// <summary>What cleaning these would report, without touching anything.</summary>
    public static CleanResult Clean(IEnumerable<Candidate> chosen) =>
        new(DateTime.UtcNow, TimeSpan.FromSeconds(2), true,
            chosen.Select(c => new CleanedItem(c.Id, c.Rule.LocalizedLabel, c.Level, c.Bytes, 100, 0, null, [])).ToList());
}
