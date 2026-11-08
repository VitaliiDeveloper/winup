using System.IO.Enumeration;

namespace Winup.Core;

public readonly record struct Measurement(long Bytes, long Files, DateTime NewestUtc, bool Partial)
{
    public static readonly Measurement Empty = new(0, 0, DateTime.MinValue, false);

    public Measurement Add(Measurement o) =>
        new(Bytes + o.Bytes, Files + o.Files, o.NewestUtc > NewestUtc ? o.NewestUtc : NewestUtc, Partial || o.Partial);
}

/// <summary>Reading the disk without ever following a link or junction.</summary>
public static class FileSystem
{
    private const FileAttributes NoSpace = FileAttributes.ReparsePoint | FileAttributes.Offline | (FileAttributes)0x400000; // RECALL_ON_DATA_ACCESS

    /// <summary>Size of a file or folder. Links count as 0 and are not entered; cloud placeholders count as 0.</summary>
    public static Measurement Measure(string path)
    {
        FileAttributes a;
        try { a = File.GetAttributes(path); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return Measurement.Empty; }
        catch { return Measurement.Empty with { Partial = true }; }
        if ((a & FileAttributes.ReparsePoint) != 0) return Measurement.Empty;
        if ((a & FileAttributes.Directory) == 0)
        {
            try
            {
                var fi = new FileInfo(path);
                return new Measurement((a & NoSpace) != 0 ? 0 : fi.Length, 1, fi.LastWriteTimeUtc, false);
            }
            catch { return Measurement.Empty with { Partial = true }; }
        }

        long bytes = 0, files = 0;
        var newest = DateTime.MinValue;
        SafeEnumerator e;
        try { e = new SafeEnumerator(path); }
        catch { return Measurement.Empty with { Partial = true }; }
        try
        {
            while (e.MoveNext())
            {
                var (size, time) = e.Current;
                bytes += size;
                files++;
                if (time > newest) newest = time;
            }
        }
        catch { e.Errors++; }
        return new Measurement(bytes, files, newest, e.Errors > 0);
    }

    /// <summary>Direct children of a folder, links included (callers decide what to do with them).</summary>
    public static IEnumerable<FileSystemInfo> Children(string dir)
    {
        try { return new DirectoryInfo(dir).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true }).ToList(); }
        catch { return Array.Empty<FileSystemInfo>(); }
    }

    public static bool IsLink(FileSystemInfo fi) => (fi.Attributes & FileAttributes.ReparsePoint) != 0;

    public static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return false; }
    }

    /// <summary>Some files under a folder (for asking Windows which programs hold them open).</summary>
    public static IEnumerable<string> SampleFiles(string dir, int max)
    {
        if (!Directory.Exists(dir) || IsLink(dir)) yield break;
        var opts = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir, "*", opts).Take(max).ToList(); }
        catch { yield break; }
        foreach (var f in files) yield return f;
    }

    /// <summary>
    /// Expands a pattern such as <c>C:\…\User Data\{Default,Profile *}\Cache</c>: <c>{a,b}</c> alternatives and
    /// <c>*</c>/<c>?</c> inside a single segment. Only existing paths come back, and the walk never enters a link.
    /// </summary>
    public static IEnumerable<string> Expand(string pattern)
    {
        foreach (var alt in ExpandBraces(pattern))
            foreach (var p in ExpandWildcards(alt))
                yield return p;
    }

    internal static IEnumerable<string> ExpandBraces(string pattern)
    {
        int open = pattern.IndexOf('{');
        if (open < 0) { yield return pattern; yield break; }
        int close = pattern.IndexOf('}', open);
        if (close < 0) { yield return pattern; yield break; }
        var head = pattern[..open];
        var tail = pattern[(close + 1)..];
        foreach (var option in pattern.Substring(open + 1, close - open - 1).Split(','))
            foreach (var rest in ExpandBraces(head + option + tail))
                yield return rest;
    }

    private static IEnumerable<string> ExpandWildcards(string pattern)
    {
        var segs = pattern.Split('\\');
        int first = Array.FindIndex(segs, s => s.IndexOfAny(['*', '?']) >= 0);
        if (first < 0)
        {
            if (Path.Exists(pattern)) yield return Paths.Normalize(pattern);
            yield break;
        }
        var bases = new List<string> { string.Join('\\', segs, 0, first) };
        for (int i = first; i < segs.Length; i++)
        {
            bool last = i == segs.Length - 1;
            var next = new List<string>();
            foreach (var b in bases)
            {
                if (!Directory.Exists(b) || IsLink(b)) continue;
                var seg = segs[i];
                if (seg.IndexOfAny(['*', '?']) < 0)
                {
                    var p = Path.Combine(b, seg);
                    if (Path.Exists(p)) next.Add(p);
                    continue;
                }
                IEnumerable<FileSystemInfo> kids = Children(b);
                foreach (var k in kids)
                {
                    if (!last && (k.Attributes & FileAttributes.Directory) == 0) continue;
                    if (IsLink(k)) continue;
                    if (FileSystemName.MatchesSimpleExpression(seg, k.Name, ignoreCase: true)) next.Add(k.FullName);
                }
            }
            bases = next;
        }
        foreach (var b in bases) yield return Paths.Normalize(b);
    }

    /// <summary>Walks a tree without entering links and counts folders it could not read.</summary>
    private sealed class SafeEnumerator : FileSystemEnumerator<(long, DateTime)>
    {
        public int Errors;

        public SafeEnumerator(string dir)
            : base(dir, new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false, ReturnSpecialDirectories = false })
        { }

        protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry) => (entry.Attributes & FileAttributes.ReparsePoint) == 0;
        protected override bool ShouldIncludeEntry(ref FileSystemEntry entry) => !entry.IsDirectory;
        protected override (long, DateTime) TransformEntry(ref FileSystemEntry entry) =>
            ((entry.Attributes & NoSpace) != 0 ? 0 : entry.Length, entry.LastWriteTimeUtc.UtcDateTime);
        protected override bool ContinueOnError(int error) { Errors++; return true; }
    }
}
