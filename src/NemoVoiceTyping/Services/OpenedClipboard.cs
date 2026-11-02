using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace NemoVoiceTyping.Services;

/// <summary>One clipboard format and its raw bytes.</summary>
internal sealed record ClipboardItem(uint Format, byte[] Data);

/// <summary>
/// The clipboard, opened for exclusive access: no other program can change it
/// until this is disposed, so saving, checking and replacing its contents
/// happen as one step.
///
/// Contents are copied format by format, so whatever the user had copied
/// (text, rich text, images, files) can be put back. Bitmaps and old-style
/// metafiles are skipped because Windows recreates them from the
/// device-independent bitmap or enhanced metafile kept alongside. Formats
/// that hold other handles (palettes, private and owner-drawn formats) can't
/// be copied as bytes. OLE's bookkeeping formats are skipped because they
/// describe a data object that is gone once the clipboard changes.
/// </summary>
internal sealed class OpenedClipboard : IDisposable
{
    private const uint CF_BITMAP = 2;
    private const uint CF_METAFILEPICT = 3;
    private const uint CF_PALETTE = 9;
    private const uint CF_UNICODETEXT = 13;
    private const uint CF_ENHMETAFILE = 14;
    private const uint CF_OWNERDISPLAY = 0x80;
    private const uint CF_DSPENHMETAFILE = 0x8E;
    private const uint CF_PRIVATEFIRST = 0x200;
    private const uint CF_GDIOBJLAST = 0x3FF;
    private const uint GMEM_MOVEABLE = 0x0002;

    private const int OpenAttempts = 5;
    private const int OpenRetryDelayMs = 20;

    private static readonly uint[] OleBookkeepingFormats =
    {
        RegisterClipboardFormatW("DataObject"),
        RegisterClipboardFormatW("Ole Private Data"),
    };

    /// <summary>
    /// Marks clipboard contents to be left out of clipboard history. The first
    /// three are Windows' own; "Clipboard Viewer Ignore" is the long-standing
    /// convention third-party managers such as Ditto check, since the Windows
    /// formats do not bind them. All of them are advisory.
    /// </summary>
    public static readonly IReadOnlyList<ClipboardItem> HistoryOptOut = new[]
    {
        "ExcludeClipboardContentFromMonitorProcessing",
        "CanIncludeInClipboardHistory",
        "CanUploadToCloudClipboard",
        "Clipboard Viewer Ignore",
    }.Select(name => new ClipboardItem(RegisterClipboardFormatW(name), new byte[4])).ToArray();

    public static ClipboardItem UnicodeText(string text) =>
        new(CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + '\0'));

    private OpenedClipboard() { }

    /// <summary>
    /// Anyone else putting something on the clipboard takes ownership of it,
    /// so this tells whether the contents are still the ones
    /// <paramref name="window"/> put there. The sequence number can't: closing
    /// the clipboard can advance it too, when Windows adds synthesised formats.
    /// </summary>
    public bool IsOwnedBy(IntPtr window) => GetClipboardOwner() == window;

    /// <summary>Returns null if another program keeps the clipboard open.</summary>
    public static OpenedClipboard? TryOpen(IntPtr owner)
    {
        for (int attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (OpenClipboard(owner)) return new OpenedClipboard();
            Thread.Sleep(OpenRetryDelayMs);
        }
        return null;
    }

    /// <summary>
    /// Copies the current contents. Returns null when the clipboard holds
    /// something but none of it could be copied, so it must not be replaced.
    /// </summary>
    public List<ClipboardItem>? Save()
    {
        var items = new List<ClipboardItem>();
        for (uint format = EnumClipboardFormats(0); format != 0; format = EnumClipboardFormats(format))
        {
            if (!IsCopyable(format)) continue;
            IntPtr handle = GetClipboardData(format);
            var data = format == CF_ENHMETAFILE ? CopyEnhancedMetafile(handle) : CopyGlobal(handle);
            if (data != null) items.Add(new ClipboardItem(format, data));
        }
        return items.Count == 0 && CountClipboardFormats() > 0 ? null : items;
    }

    /// <summary>Replaces the contents with exactly <paramref name="items"/>; none leaves it empty.</summary>
    public bool Replace(IEnumerable<ClipboardItem> items)
    {
        if (!EmptyClipboard()) return false;
        bool all = true;
        foreach (var item in items)
            all &= item.Format == CF_ENHMETAFILE ? SetEnhancedMetafile(item.Data) : SetGlobal(item);
        return all;
    }

    public void Dispose() => CloseClipboard();

    private static bool IsCopyable(uint format) => format switch
    {
        CF_BITMAP or CF_METAFILEPICT or CF_PALETTE => false,
        >= CF_OWNERDISPLAY and <= CF_DSPENHMETAFILE => false,
        >= CF_PRIVATEFIRST and <= CF_GDIOBJLAST => false,
        _ => Array.IndexOf(OleBookkeepingFormats, format) < 0,
    };

    private static byte[]? CopyGlobal(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return null;
        nuint size = GlobalSize(handle);
        if (size == 0 || size > int.MaxValue) return null;
        IntPtr source = GlobalLock(handle);
        if (source == IntPtr.Zero) return null;
        try
        {
            var data = new byte[(int)size];
            Marshal.Copy(source, data, 0, data.Length);
            return data;
        }
        finally { GlobalUnlock(handle); }
    }

    private static byte[]? CopyEnhancedMetafile(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return null;
        uint size = GetEnhMetaFileBits(handle, 0, null);
        if (size == 0) return null;
        var data = new byte[size];
        return GetEnhMetaFileBits(handle, size, data) == size ? data : null;
    }

    /// <summary>On success the clipboard owns the memory; on failure it is freed here.</summary>
    private static bool SetGlobal(ClipboardItem item)
    {
        IntPtr handle = GlobalAlloc(GMEM_MOVEABLE, (nuint)item.Data.Length);
        if (handle == IntPtr.Zero) return false;
        IntPtr target = GlobalLock(handle);
        if (target == IntPtr.Zero)
        {
            GlobalFree(handle);
            return false;
        }
        Marshal.Copy(item.Data, 0, target, item.Data.Length);
        GlobalUnlock(handle);

        if (SetClipboardData(item.Format, handle) != IntPtr.Zero) return true;
        GlobalFree(handle);
        return false;
    }

    private static bool SetEnhancedMetafile(byte[] data)
    {
        IntPtr handle = SetEnhMetaFileBits((uint)data.Length, data);
        if (handle == IntPtr.Zero) return false;
        if (SetClipboardData(CF_ENHMETAFILE, handle) != IntPtr.Zero) return true;
        DeleteEnhMetaFile(handle);
        return false;
    }

    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern int CountClipboardFormats();
    [DllImport("user32.dll")] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr hMem);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormatW(string format);

    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(IntPtr hMem);

    [DllImport("gdi32.dll")] private static extern uint GetEnhMetaFileBits(IntPtr hEmf, uint size, byte[]? data);
    [DllImport("gdi32.dll")] private static extern IntPtr SetEnhMetaFileBits(uint size, byte[] data);
    [DllImport("gdi32.dll")] private static extern bool DeleteEnhMetaFile(IntPtr hEmf);
}
