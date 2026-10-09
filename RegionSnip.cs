using System.Diagnostics;
using System.Runtime.InteropServices;
using Clipboard = System.Windows.Clipboard;

namespace ScreenSearchOverlay;

// A region of the screen, picked with Windows' own snipping overlay (the one
// behind Win+Shift+S): every monitor, every DPI, rectangle, window or free
// form, for free. The overlay puts its image in the clipboard; this waits
// for it. UI thread: the clipboard needs STA.
//
// Windows says nothing when the user cancels (Esc). The overlay's process
// closing without an image is that signal: ScreenClippingHost on Windows 10,
// SnippingTool on recent Windows 11. A SnippingTool already running before
// the click may linger after a cancel: the wait then ends on the timeout, or
// as soon as the caller gives up (Ctrl+Alt+G brought its window back).
internal static partial class RegionSnip
{
    private const string SnipUri = "ms-screenclip:";
    private static readonly string[] OverlayProcesses = ["ScreenClippingHost", "SnippingTool"];
    private const int TimeoutMs = 120_000;
    private const int PollMs = 100;
    // The image is written right before the overlay closes: a moment's grace.
    private const int LastImageGraceMs = 300;
    // The overlay still holds the screen and the foreground for a moment
    // after its image is out.
    private const int OverlayCloseTimeoutMs = 2_000;

    // True once a new image is in the clipboard; false when the user
    // canceled, the overlay couldn't start, or abandoned() turned true.
    internal static async Task<bool> RunAsync(Func<bool> abandoned)
    {
        var before = SelectionGrabber.ClipboardSequence;
        var alreadyRunning = OverlayIds();
        try { Process.Start(new ProcessStartInfo(SnipUri) { UseShellExecute = true }); }
        catch { return false; }

        bool NewImage() => SelectionGrabber.ClipboardSequence != before && HasImage();

        var seen = new HashSet<int>();
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < TimeoutMs && !abandoned())
        {
            await Task.Delay(PollMs);
            if (NewImage())
            {
                await OverlayGoneAsync();
                return true;
            }

            var open = OverlayIds();
            open.ExceptWith(alreadyRunning);
            seen.UnionWith(open);
            if (seen.Count > 0 && open.Count == 0)
            {
                await Task.Delay(LastImageGraceMs);
                var taken = NewImage();
                await OverlayGoneAsync();
                return taken;
            }
        }
        return false;
    }

    // Coming back while the overlay is still in front would be refused by
    // Windows (the window would only flash in the taskbar): wait until the
    // foreground no longer belongs to it.
    private static async Task OverlayGoneAsync()
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < OverlayCloseTimeoutMs)
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundId);
            if (!OverlayIds().Contains((int)foregroundId)) return;
            await Task.Delay(PollMs / 2);
        }
    }

    private static HashSet<int> OverlayIds()
    {
        var ids = new HashSet<int>();
        foreach (var name in OverlayProcesses)
            foreach (var process in Process.GetProcessesByName(name))
                using (process) ids.Add(process.Id);
        return ids;
    }

    private static bool HasImage()
    {
        try { return Clipboard.ContainsImage(); }
        catch { return false; } // held by the overlay this instant: next poll
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
