namespace Winup.Core;

/// <summary>
/// The last word on every delete. Lives in code, not in rules.json, so a bad or edited rule
/// cannot widen it: a path is deleted only if it is inside one of the allowed places below,
/// inside the folder its rule matched, not protected, and no folder on the way is a link.
/// </summary>
public sealed class SafetyPolicy
{
    private readonly PathEnvironment _env;
    private readonly string[] _allowed;
    private readonly HashSet<string> _denyExact;
    private readonly string[] _denyUnder;

    /// <summary>Folder names that hold settings, sign-ins or unsaved work, wherever they appear.</summary>
    public static readonly IReadOnlySet<string> ProtectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "User",            // VS Code / Electron settings, snippets, workspace state
        "Backups",         // Electron: unsaved files
        "User Data",       // a browser's whole profile set (only cache folders inside it are allowed)
        "Login Data", "Cookies", "Network", "Local Storage", "Session Storage", "IndexedDB",
        "databases", "Sessions", "Extensions", "Bookmarks", "History",
        ".ssh", ".gnupg", ".aws", ".azure", ".kube", ".docker",
    };

    /// <summary>Chromium / Electron / Firefox cache folders. Inside a browser's User Data only these may be emptied.</summary>
    public static readonly IReadOnlySet<string> CacheFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache", "ShaderCache",
        "GrShaderCache", "GraphiteDawnCache", "CachedData", "CachedExtensionVSIXs", "component_crx_cache", "cache2",
    };

    /// <summary>Virtual disks of WSL, Docker and VMs: never, not even inside a cache.</summary>
    public static readonly IReadOnlySet<string> ProtectedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".vhd", ".vhdx", ".avhd", ".avhdx", ".vmdk", ".vdi", ".qcow2",
    };

    public SafetyPolicy(PathEnvironment env)
    {
        _env = env;
        string L = env[PathEnvironment.LocalAppData], R = env[PathEnvironment.RoamingAppData], LL = env[PathEnvironment.LocalLow];
        string U = env[PathEnvironment.UserProfile], PD = env[PathEnvironment.ProgramData], W = env[PathEnvironment.Windows];
        string SD = env[PathEnvironment.SystemDrive];

        _allowed = Clean(
            L, R, LL, env[PathEnvironment.Temp],
            Path.Combine(U, ".nuget"), Path.Combine(U, ".gradle"), Path.Combine(U, ".m2"), Path.Combine(U, ".cargo"),
            Path.Combine(U, ".bun"), Path.Combine(U, ".cache"),
            Path.Combine(PD, "NVIDIA Corporation", "Downloader"),
            Path.Combine(PD, "NVIDIA Corporation", "NVIDIA App", "UpdateFramework", "ota-artifacts"),
            Path.Combine(PD, "Microsoft", "Windows", "WER"),
            Path.Combine(W, "Temp"), Path.Combine(W, "Minidump"), Path.Combine(W, "LiveKernelReports"),
            Path.Combine(W, "MEMORY.DMP"),
            Path.Combine(SD, "NVIDIA"));

        _denyExact = new HashSet<string>(Clean(
            SD, W, PD, U, Path.Combine(U, "AppData"), L, R, LL, env[PathEnvironment.Temp],
            Path.Combine(L, "Microsoft"), Path.Combine(R, "Microsoft"), Path.Combine(L, "Programs"),
            Path.Combine(L, "Microsoft", "Windows"),
            Path.Combine(SD, "Program Files"), Path.Combine(SD, "Program Files (x86)"), Path.Combine(SD, "Users")),
            StringComparer.OrdinalIgnoreCase);

        _denyUnder = Clean(new[]
        {
            Path.Combine(W, "System32"), Path.Combine(W, "SysWOW64"), Path.Combine(W, "WinSxS"), Path.Combine(W, "Installer"),
            Path.Combine(W, "servicing"), Path.Combine(SD, "$Recycle.Bin"), Path.Combine(SD, "System Volume Information"),
            Path.Combine(SD, "Recovery"), Path.Combine(PD, "Package Cache"),
            Path.Combine(L, "Packages"), Path.Combine(L, "wsl"), Path.Combine(L, "Docker"),
            Path.Combine(L, "Microsoft", "WindowsApps"), Path.Combine(L, "Microsoft", "Credentials"),
            Path.Combine(R, "Microsoft", "Credentials"), Path.Combine(R, "Microsoft", "Protect"),
            Path.Combine(L, "Microsoft", "Outlook"),
            env.DataDir,
        }.Concat(env.UserFolders).ToArray());
    }

    public IReadOnlyList<string> AllowedPlaces => _allowed;

    /// <summary>
    /// Why <paramref name="path"/> must not be deleted, or null if it may go.
    /// <paramref name="scope"/> is the folder the rule matched; for <see cref="CleanKind.Contents"/>
    /// the path must be strictly inside it, for <see cref="CleanKind.Paths"/> it must be it.
    /// Touches the disk (attributes of the path and its parents) and is meant to run right before deleting.
    /// </summary>
    public string? WhyRefused(string path, string scope, CleanKind kind)
    {
        if (string.IsNullOrWhiteSpace(path)) return "empty path";
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return "network or device path";
        if (path.Contains(@"\..\", StringComparison.Ordinal) || path.EndsWith(@"\..", StringComparison.Ordinal) ||
            path.Contains("/..", StringComparison.Ordinal)) return "path with ..";
        string full, sc;
        try { full = Paths.Normalize(path); sc = Paths.Normalize(scope); }
        catch { return "invalid path"; }
        if (full.Length <= 3) return "drive root";

        if (kind == CleanKind.Contents ? !Paths.IsStrictlyUnder(full, sc) : !full.Equals(sc, StringComparison.OrdinalIgnoreCase))
            return "outside the rule's folder";

        string? place = _allowed.FirstOrDefault(a => Paths.IsStrictlyUnder(full, a) || full.Equals(a, StringComparison.OrdinalIgnoreCase) && IsAllowedLeaf(a));
        if (place is null) return "not in an allowed place";
        if (_denyExact.Contains(full)) return "protected folder";
        if (_denyUnder.Any(d => Paths.IsSameOrUnder(full, d))) return "protected area";

        var rel = full.Length > place.Length ? full[(place.Length + 1)..] : "";
        var segs = rel.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segs.Length; i++)
        {
            if (!ProtectedNames.Contains(segs[i])) continue;
            // a browser's "User Data" is fine only below one of its cache folders
            bool cacheBelow = segs[i].Equals("User Data", StringComparison.OrdinalIgnoreCase) &&
                              segs.Skip(i + 1).Take(segs.Length - i - 2).Any(s => CacheFolderNames.Contains(s));
            if (!cacheBelow) return $"protected name '{segs[i]}'";
        }
        if (ProtectedExtensions.Contains(Path.GetExtension(full))) return "virtual disk";

        // never through a link: the path itself and every folder between it and the allowed place
        for (var p = full; p.Length >= place.Length; p = Path.GetDirectoryName(p) ?? "")
        {
            FileAttributes a;
            try { a = File.GetAttributes(p); }
            catch (FileNotFoundException) { return p == full ? "gone" : "parent gone"; }
            catch (DirectoryNotFoundException) { return p == full ? "gone" : "parent gone"; }
            catch (Exception e) { return "cannot read attributes: " + e.GetType().Name; }
            if ((a & FileAttributes.ReparsePoint) != 0) return p == full ? "link or junction" : "inside a link or junction";
            if (p.Length == place.Length) break;
        }

        if (Directory.Exists(full) && ContainsVirtualDisk(full)) return "contains a virtual disk";
        return null;
    }

    /// <summary>An allowed place that is a single file (MEMORY.DMP) may itself be deleted.</summary>
    private static bool IsAllowedLeaf(string allowed) => Path.HasExtension(allowed) && allowed.EndsWith(".DMP", StringComparison.OrdinalIgnoreCase);

    /// <summary>True if a rule's pattern root lies inside the allowed places (used by tests to keep rules honest).</summary>
    public bool IsInAllowedPlace(string path)
    {
        var full = Paths.Normalize(path);
        return _allowed.Any(a => Paths.IsSameOrUnder(full, a));
    }

    /// <summary>True if the path, or a folder between it and its allowed place, is a link or junction (or it is in no allowed place).</summary>
    public bool HasLinkOnTheWay(string path)
    {
        var full = Paths.Normalize(path);
        var place = _allowed.Where(a => Paths.IsSameOrUnder(full, a)).OrderBy(a => a.Length).FirstOrDefault();
        if (place is null) return true;
        for (var p = full; p.Length >= place.Length; p = Path.GetDirectoryName(p) ?? "")
        {
            if (FileSystem.IsLink(p)) return true;
            if (p.Length == place.Length) break;
        }
        return false;
    }

    private static bool ContainsVirtualDisk(string dir)
    {
        try
        {
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            return Directory.EnumerateFiles(dir, "*", opts).Any(f => ProtectedExtensions.Contains(Path.GetExtension(f)));
        }
        catch { return true; } // cannot tell: refuse
    }

    private static string[] Clean(params string[] paths) =>
        paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Paths.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
