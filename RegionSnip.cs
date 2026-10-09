using System.Diagnostics;
using System.Runtime.InteropServices;
using Clipboard = System.Windows.Clipboard;

namespace ScreenSearchOverlay;

// A region of the screen, picked with Windows' own snipping overlay (the one
// behind Win+Shift+S): every monitor, every DPI, rectangle, window or free
// form, for free. The overlay puts its image in the clipboard; this waits
// for it. UI thread: the clipboard needs STA.
//
// Windows says nothing when the user cancels (Esc). The overlay closing
// without an image is that signal — its process ending (ScreenClippingHost
// on Windows 10), or it leaving the foreground (SnippingTool on Windows 11,
// which may stay running). The timeout and the caller giving up (Ctrl+Alt+G
// brought its window back) are the last resorts.
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
        var overlayWasInFront = false;
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < TimeoutMs && !abandoned())
        {
            await Task.Delay(PollMs);
            if (NewImage())
            {
                await OverlayGoneAsync();
                return true;
            }

            // Two signs of an end without an image: the overlay's process
            // closed (Windows 10), or the overlay left the foreground — which
            // also covers a SnippingTool that stays running (Windows 11).
            var overlays = OverlayIds();
            var inFront = overlays.Contains(ForegroundProcessId());
            overlayWasInFront |= inFront;
            overlays.ExceptWith(alreadyRunning);
            seen.UnionWith(overlays);
            if ((seen.Count > 0 && overlays.Count == 0) || (overlayWasInFront && !inFront))
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
            if (!OverlayIds().Contains(ForegroundProcessId())) return;
            await Task.Delay(PollMs / 2);
        }
    }

    private static int ForegroundProcessId()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var id);
        return (int)id;
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
