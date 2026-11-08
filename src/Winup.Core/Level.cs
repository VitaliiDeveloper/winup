namespace Winup.Core;

/// <summary>How much a cleanup costs you afterwards. Same three levels as macup.</summary>
public enum Level
{
    /// <summary>Rebuilds itself, nothing to notice.</summary>
    Safe = 1,
    /// <summary>Downloaded again on next use (time, traffic).</summary>
    Standard = 2,
    /// <summary>Apps get slower for a while or sign you out.</summary>
    Aggressive = 3,
}

public static class Levels
{
    public static bool TryParse(string? s, out Level level)
    {
        switch (s?.Trim().ToLowerInvariant())
        {
            case "1" or "safe": level = Level.Safe; return true;
            case "2" or "standard": level = Level.Standard; return true;
            case "3" or "aggressive": level = Level.Aggressive; return true;
            default: level = Level.Safe; return false;
        }
    }

    public static string Key(this Level level) => level switch
    {
        Level.Safe => "safe",
        Level.Standard => "standard",
        _ => "aggressive",
    };

    /// <summary>Localized level word.</summary>
    public static string Word(this Level level) => Loc.T(level.Key());
}

/// <summary>What happens to a rule's folders.</summary>
public enum CleanKind
{
    /// <summary>Delete what is inside the folder; the folder itself stays.</summary>
    Contents,
    /// <summary>Delete the matched files or folders themselves.</summary>
    Paths,
}
