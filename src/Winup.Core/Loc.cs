using System.Globalization;
using System.Text.Json;

namespace Winup.Core;

/// <summary>Interface strings in English, Ukrainian and Russian. Keys are the English text (as in macup).</summary>
public static class Loc
{
    public static readonly IReadOnlyList<string> Languages = ["en", "uk", "ru"];

    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Table = new(Load);
    private static string _lang = Detect();

    public static string Lang
    {
        get => _lang;
        set => _lang = Languages.Contains(value) ? value : "en";
    }

    public static CultureInfo Culture => _lang switch
    {
        "uk" => CultureInfo.GetCultureInfo("uk-UA"),
        "ru" => CultureInfo.GetCultureInfo("ru-RU"),
        _ => CultureInfo.GetCultureInfo("en-US"),
    };

    public static string Detect()
    {
        var env = Environment.GetEnvironmentVariable("WINUP_LANG");
        if (env is not null && Languages.Contains(env)) return env;
        var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Languages.Contains(two) ? two : "en";
    }

    public static string T(string en)
    {
        if (_lang == "en") return en;
        return Table.Value.TryGetValue(en, out var t) && t.TryGetValue(_lang, out var s) && s.Length > 0 ? s : en;
    }

    public static string F(string en, params object?[] args) => string.Format(Culture, T(en), args);

    /// <summary>English keys that miss a translation (tests keep this empty).</summary>
    public static IEnumerable<string> Missing(IEnumerable<string> keys) =>
        keys.Distinct().Where(k => !Table.Value.TryGetValue(k, out var t) || Languages.Skip(1).Any(l => !t.ContainsKey(l) || t[l].Length == 0));

    public static IReadOnlyCollection<string> Keys => Table.Value.Keys;

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        using var s = typeof(Loc).Assembly.GetManifestResourceStream("Winup.Core.strings.json")
            ?? throw new InvalidOperationException("strings.json is not embedded");
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(s,
            new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) ?? [];
    }
}

public static class Format
{
    public static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        var num = u == 0 ? v.ToString("0", Loc.Culture) : v.ToString(v < 10 ? "0.0" : "0", Loc.Culture);
        return num + " " + Loc.T(units[u]);
    }
}
