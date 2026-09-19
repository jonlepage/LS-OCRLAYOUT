using System.Windows;
using System.Windows.Input;

namespace ScreenSearchOverlay;

// Scroll state machine — see header comment in BeginScrollSession.
public partial class MainWindow : Window
{
    private const int ScrollDebounceMs = 400;
    private const int ScrollSettleMs = 50;

    // ─── Scroll state machine ─────────────────────────────────────────────
    // Idle:      content visible, click-through OFF, search bar interactive
    // Scrolling: content hidden, click-through ON (WS_EX_TRANSPARENT), the
    //            global mouse hook keeps the debounce alive.
    //
    // Why this design: Chromium/Electron ignore synthetic WM_MOUSEWHEEL. The
    // only way to scroll them is real OS-level wheel input. By going
    // click-through during a scroll session, the user's physical wheels go
    // natively to the app underneath. We can't listen via WPF anymore (we
    // don't receive events while click-through), so a global low-level hook
    // (WH_MOUSE_LL) keeps the debounce alive.

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Only entered when click-through is OFF — start of a session.
        if (!_scrollSessionActive)
            BeginScrollSession(e.Delta);
        e.Handled = true;
    }

    private void OnGlobalWheelDetected()
    {
        // Hook fires for every wheel system-wide. Only react when the session
        // is active AND the cursor is over our window (multi-monitor sanity).
        if (!_scrollSessionActive) return;
        if (!IsCursorOverThisWindow()) return;

        _wheelGeneration++;
        try { _scrollDebounce.Change(ScrollDebounceMs, System.Threading.Timeout.Infinite); }
        catch (Exception ex) { Log($"hook timer-change EX: {ex}"); }
    }

    private void BeginScrollSession(int firstDelta)
    {
        Log($"session BEGIN delta={firstDelta}");
        _scrollSessionActive = true;
        _wheelGeneration++;

        OverlayContent.Visibility = Visibility.Hidden;

        // Click-through MUST be set BEFORE injecting, so the injected wheel
        // hits the app below (not us → no self-loop).
        SetClickThrough(true);

        try { InjectMouseWheel(firstDelta); }
        catch (Exception ex) { Log($"InjectMouseWheel EX: {ex}"); }

        try { _scrollDebounce.Change(ScrollDebounceMs, System.Threading.Timeout.Infinite); }
        catch (Exception ex) { Log($"timer-change EX: {ex}"); }
    }

    private async void OnScrollIdle()
    {
        if (!_scrollSessionActive) return;

        var myGen = _wheelGeneration;
        Log($"idle enter gen={myGen}");

        try
        {
            // Let smooth-scroll animations settle
            await Task.Delay(ScrollSettleMs);
            if (_wheelGeneration != myGen) { Log("idle: aborted (settle)"); return; }

            // CaptureScreen runs on a background thread so the UI thread stays
            // responsive. The pixel push into the WriteableBitmap MUST happen
            // on the UI thread (WriteableBitmap has thread affinity), but it's
            // a single memcpy — sub-5 ms even at 4K.
            var sx = _screenInfo.X;
            var sy = _screenInfo.Y;
            var sw = _screenInfo.Width;
            var sh = _screenInfo.Height;
            var newBmp = await Task.Run(() => CaptureScreen(sx, sy, sw, sh));

            if (_wheelGeneration != myGen)
            {
                // User started scrolling again — throw away the work
                newBmp.Dispose();
                Log("idle: aborted (capture)");
                return;
            }

            // Atomic swap on UI thread + dispose the previous frame to keep
            // GDI handle / native memory usage flat across many sessions.
            HighlightCanvas.Children.Clear();
            ClearTranslation();
            var oldBmp = _screenshot;
            _screenshot = newBmp;
            UpdateScreenshotBitmap(newBmp);
            oldBmp?.Dispose();

            // ── Transition Scrolling → Idle ──
            SetClickThrough(false);
            OverlayContent.Visibility = Visibility.Visible;
            _scrollSessionActive = false;
            Log("session END");

            // Cache check: if the captured pixels hash to the same value as
            // the previous frame, the OCR result is still valid — skip the
            // expensive OCR pass and just re-apply existing highlights.
            var newHash = ComputeHash(newBmp);
            if (newHash == _lastScreenshotHash && _ocrWords.Count > 0)
            {
                Log("OCR cache HIT");
                RefreshOverlayContent();
                return;
            }
            _lastScreenshotHash = newHash;

            // OCR is expensive (enhance + convert + recognize). Runs entirely
            // off the UI thread; we only touch UI again to apply highlights.
            _ocrTask = RunOcrAsync(newBmp);
            await _ocrTask;
            if (_wheelGeneration != myGen) { Log("OCR: aborted (new session)"); return; }
            RefreshOverlayContent();
        }
        catch (Exception ex)
        {
            Log($"OnScrollIdle EX: {ex}");
            // Safety net: never strand the user with a click-through invisible window
            try { SetClickThrough(false); OverlayContent.Visibility = Visibility.Visible; } catch { }
            _scrollSessionActive = false;
        }
    }
}
