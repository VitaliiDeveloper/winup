using System.Runtime.InteropServices;

namespace Winup.Core;

/// <summary>
/// The folders rules talk about (%LOCALAPPDATA%, %TEMP%, …). Tests build one over a fake tree,
/// so nothing a test does can reach the real profile.
/// </summary>
public sealed class PathEnvironment
{
    public const string LocalAppData = "LOCALAPPDATA";
    public const string RoamingAppData = "APPDATA";
    public const string LocalLow = "LOCALLOW";
    public const string Temp = "TEMP";
    public const string UserProfile = "USERPROFILE";
    public const string ProgramData = "PROGRAMDATA";
    public const string Windows = "WINDIR";
    public const string SystemDrive = "SYSTEMDRIVE";

    private readonly Dictionary<string, string> _tokens;

    public PathEnvironment(IDictionary<string, string> tokens, IEnumerable<string> userFolders, bool isElevated)
    {
        _tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in tokens)
        {
            if (!string.IsNullOrWhiteSpace(v)) _tokens[k] = Paths.Normalize(v);
        }
        UserFolders = userFolders.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Paths.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        IsElevated = isElevated;
    }

    public IReadOnlyDictionary<string, string> Tokens => _tokens;

    /// <summary>Documents, Desktop, Downloads, Pictures, Music, Videos, OneDrive: never touched.</summary>
    public IReadOnlyList<string> UserFolders { get; }

    public bool IsElevated { get; }

    public string this[string token] => _tokens.TryGetValue(token, out var v) ? v : "";

    /// <summary>winup's own settings, reports and history.</summary>
    public string DataDir => Path.Combine(this[LocalAppData], "winup");

    /// <summary>Replaces %TOKEN% with its folder. Returns null when a token is unknown.</summary>
    public string? Expand(string pattern)
    {
        var result = pattern;
        int start;
        while ((start = result.IndexOf('%')) >= 0)
        {
            int end = result.IndexOf('%', start + 1);
            if (end < 0) return null;
            var name = result.Substring(start + 1, end - start - 1);
            if (!_tokens.TryGetValue(name, out var value)) return null;
            result = string.Concat(result.AsSpan(0, start), value, result.AsSpan(end + 1));
        }
        return result;
    }

    /// <summary>The real folders of the signed-in user.</summary>
    public static PathEnvironment Current()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var tokens = new Dictionary<string, string>
        {
            [LocalAppData] = local,
            [RoamingAppData] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            [LocalLow] = KnownFolder(new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16")) ?? Path.Combine(profile, "AppData", "LocalLow"),
            [Temp] = Path.GetTempPath(),
            [UserProfile] = profile,
            [ProgramData] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            [Windows] = windows,
            [SystemDrive] = Path.GetPathRoot(windows) ?? "C:\\",
        };
        var user = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            KnownFolder(new Guid("374DE290-123F-4565-9164-39C4925E467B")) ?? Path.Combine(profile, "Downloads"),
            Environment.GetEnvironmentVariable("OneDrive") ?? "",
            Environment.GetEnvironmentVariable("OneDriveConsumer") ?? "",
            Environment.GetEnvironmentVariable("OneDriveCommercial") ?? "",
        };
        return new PathEnvironment(tokens, user, Elevation.IsElevated());
    }

    /// <summary>A Windows-like tree under <paramref name="root"/> for tests.</summary>
    public static PathEnvironment ForFakeRoot(string root, bool isElevated = false)
    {
        string profile = Path.Combine(root, "Users", "me");
        string local = Path.Combine(profile, "AppData", "Local");
        var tokens = new Dictionary<string, string>
        {
            [LocalAppData] = local,
            [RoamingAppData] = Path.Combine(profile, "AppData", "Roaming"),
            [LocalLow] = Path.Combine(profile, "AppData", "LocalLow"),
            [Temp] = Path.Combine(local, "Temp"),
            [UserProfile] = profile,
            [ProgramData] = Path.Combine(root, "ProgramData"),
            [Windows] = Path.Combine(root, "Windows"),
            [SystemDrive] = root,
        };
        var user = new[] { "Desktop", "Documents", "Pictures", "Music", "Videos", "Downloads", "OneDrive" }
            .Select(n => Path.Combine(profile, n));
        return new PathEnvironment(tokens, user, isElevated);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(in Guid rfid, uint flags, IntPtr token, out IntPtr path);

    private static string? KnownFolder(Guid id)
    {
        try
        {
            if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var p) != 0) return null;
            try { return Marshal.PtrToStringUni(p); }
            finally { Marshal.FreeCoTaskMem(p); }
        }
        catch { return null; }
    }
}

public static class Elevation
{
    public static bool IsElevated()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}

public static class Paths
{
    /// <summary>Full path without a trailing separator (except for a drive root).</summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.Length > 3) full = full.TrimEnd('\\', '/');
        return full;
    }

    public static bool IsSameOrUnder(string path, string root)
    {
        if (path.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
        var r = root.EndsWith('\\') ? root : root + "\\";
        return path.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStrictlyUnder(string path, string root) =>
        !path.Equals(root, StringComparison.OrdinalIgnoreCase) && IsSameOrUnder(path, root);
}
