using System.Runtime.InteropServices;

namespace Winup.Core;

/// <summary>Where deleted things go. The real one is the Recycle Bin; tests move them into a fake bin.</summary>
public interface IRecycler
{
    /// <summary>Removes one file or folder. Returns null on success, otherwise why not.</summary>
    string? Recycle(string path);
}

/// <summary>Sends to the Recycle Bin with the Windows shell, the same way Explorer's Delete does.</summary>
public sealed class RecycleBin : IRecycler
{
    public string? Recycle(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0\0",
            // ALLOWUNDO: to the Recycle Bin. WANTNUKEWARNING: if something cannot go there (too big, no bin on
            // that drive), Windows asks instead of silently deleting it for good.
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING,
        };
        int r;
        try { r = SHFileOperationW(ref op); }
        catch (Exception e) { return e.Message; }
        if (!Path.Exists(path)) return null;
        if (op.fAnyOperationsAborted) return "cancelled";
        return r == 0 ? "still there (in use?)" : $"in use or not allowed (code 0x{r:X})";
    }

    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004, FOF_NOCONFIRMATION = 0x0010, FOF_ALLOWUNDO = 0x0040, FOF_NOERRORUI = 0x0400, FOF_WANTNUKEWARNING = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);
}
