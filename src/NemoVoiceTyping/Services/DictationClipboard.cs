using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;

namespace NemoVoiceTyping.Services;

/// <summary>
/// Pastes dictated text through the clipboard, then puts back whatever the
/// user had copied once dictation stops.
///
/// Clipboard work runs on one dedicated thread with a message loop, whose
/// hidden window owns the clipboard while dictated text is on it. Each paste,
/// from writing the clipboard to pressing Ctrl+V, runs there as one step, so a
/// restore can't land in the middle of it. Callers wait for it to finish, so a
/// paste stays in order with the keystrokes around it. The UI thread can't
/// host this: it waits on the dictation processor's lock while stopping, which
/// a paste can be holding.
/// </summary>
internal static class DictationClipboard
{
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    /// <summary>
    /// Pause after each Ctrl+V so the target reads this text from the
    /// clipboard before the next word or the restore replaces it.
    /// </summary>
    private const int PasteSettleMs = 25;

    private static readonly Lazy<Dispatcher> Worker = new(StartWorker);
    private static HwndSource? _owner;

    /// <summary>What the user had copied before the first paste; null when nothing is borrowed.</summary>
    private static List<ClipboardItem>? _saved;

    private static IntPtr Owner => _owner!.Handle;

    /// <summary>
    /// Puts <paramref name="text"/> on the clipboard, saving what was there
    /// first, then calls <paramref name="pressPaste"/>. Returns false, without
    /// pasting, if the clipboard can't be saved or written.
    /// </summary>
    public static bool TryPaste(string text, Action pressPaste)
    {
        try { return Worker.Value.Invoke(() => PasteOnWorker(text, pressPaste)); }
        catch (Exception ex) { Debug.WriteLine(ex); return false; }
    }

    /// <summary>
    /// Puts back what the user had copied before dictation, unless they have
    /// copied something else since. If the clipboard is busy the saved copy is
    /// kept and the next call tries again, so calling it more than once is
    /// safe: the controller calls it when dictation stops, and again when the
    /// speech worker finishes, since a backlog can outlast the stop.
    /// </summary>
    public static void Restore()
    {
        if (!Worker.IsValueCreated) return;
        try { Worker.Value.Invoke(RestoreOnWorker); }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    private static bool PasteOnWorker(string text, Action pressPaste)
    {
        using (var clipboard = OpenedClipboard.TryOpen(Owner))
        {
            if (clipboard == null) return false;
            if (_saved == null || !clipboard.IsOwnedBy(Owner)) _saved = clipboard.Save();
            if (_saved == null) return false;
            if (!clipboard.Replace([OpenedClipboard.UnicodeText(text), .. OpenedClipboard.HistoryOptOut]))
                return false;
        }
        pressPaste();
        Thread.Sleep(PasteSettleMs);
        return true;
    }

    private static void RestoreOnWorker()
    {
        if (_saved == null) return;
        using var clipboard = OpenedClipboard.TryOpen(Owner);
        if (clipboard == null) return;
        if (!clipboard.IsOwnedBy(Owner))
        {
            _saved = null;
            return;
        }

        List<ClipboardItem> restored = _saved.Count == 0
            ? _saved
            : [.. _saved, .. OpenedClipboard.HistoryOptOut];
        if (clipboard.Replace(restored)) _saved = null;
    }

    private static Dispatcher StartWorker()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            _owner = new HwndSource(new HwndSourceParameters("Nemo Voice Typing clipboard")
            {
                ParentWindow = HWND_MESSAGE,
                WindowStyle = 0,
            });
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Dictation Clipboard",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}
