using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Color = System.Windows.Media.Color;

namespace ScreenSearchOverlay;

// ChatGPT in a WebView2, never closed (closing hides it) so its page and
// session stay warm.
//
// Latency: every Ctrl+Alt+G preheats it — a blank conversation loads in the
// background while a prompt is picked — so Send usually finds the composer
// ready and only has to type. Nothing exists before the first Ctrl+Alt+G:
// the app stays as light as before until the tool is actually used.
//
// The page is remote content: no host objects are exposed to it, and the
// only channel back is chrome.webview.postMessage, read by OnWebMessage.
public partial class ChatWindow : Window
{
    private const int PageReadyTimeoutMs = 20_000;
    private const int NavigationTimeoutMs = 20_000;
    private const int ConsentReloadTimeoutMs = 8_000;
    private const int ComposerPollMs = 40;
    // Upper bound for the whole in-page send; the script has its own,
    // shorter deadlines and normally reports long before.
    private const int SendResultTimeoutMs =
        ChatGptPage.PageBootTimeoutMs + ChatGptPage.ImageUploadTimeoutMs * 2 + ChatGptPage.SendTimeoutMs + 10_000;

    // A hidden page must not be slowed down: its load runs while the
    // window is hidden (the preheat), and the send script's waits are timers.
    // Chromium's equivalent of LSDE2's backgroundThrottling: false.
    private const string BrowserArguments =
        "--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows";

    private readonly string _userDataFolder;
    private readonly Action<Rect> _saveBounds;
    private readonly Action _backToPromptBuilder;
    // The setting, read at every send: it can change between two.
    private readonly Func<bool> _temporaryChat;
    private readonly Dictionary<string, TaskCompletionSource<JsonElement>> _pending = [];
    private readonly DispatcherTimer _statusHide = new() { Interval = TimeSpan.FromSeconds(4) };

    private Task? _initialization;
    private Task? _preheat;
    // The page sits on a blank conversation, untouched, ready to receive.
    private bool _pristine;
    // The mode the page was last loaded in; null until it was loaded at all.
    private bool? _preparedTemporary;
    private bool _sending;
    private bool _closingForGood;
    // Shown once, off-screen, so the WebView could initialize (see RealizeOffScreen).
    private bool _realized;

    internal ChatWindow(string userDataFolder, Rect? bounds, Action<Rect> saveBounds, Action backToPromptBuilder, Func<bool> temporaryChat)
    {
        InitializeComponent();
        _userDataFolder = userDataFolder;
        _saveBounds = saveBounds;
        _backToPromptBuilder = backToPromptBuilder;
        _temporaryChat = temporaryChat;

        WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x21, 0x21, 0x21);
        _statusHide.Tick += (_, _) => { _statusHide.Stop(); StatusBar.Visibility = Visibility.Collapsed; };
        SourceInitialized += (_, _) => ConfigureNativeWindow();

        if (bounds is { } b && IsOnScreen(b))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = b.X;
            Top = b.Y;
            Width = b.Width;
            Height = b.Height;
        }
    }

    // ── Preheat ──

    // Called on every Ctrl+Alt+G. Hidden and already blank: nothing to do.
    // Visible, the page may show an answer or something typed by hand, and
    // the coming send starts a new conversation anyway: start it now. Same
    // when the temporary chat setting changed since the page was loaded.
    internal void Preheat()
    {
        if (_sending || _preheat is { IsCompleted: false }) return;
        if (_pristine && !IsVisible && _preparedTemporary == _temporaryChat()) return;
        _preheat = PreheatAsync();
    }

    private async Task PreheatAsync()
    {
        try
        {
            RealizeOffScreen();
            await EnsureInitializedAsync();
            _pristine = false;
            _pristine = await PrepareConversationAsync();
        }
        catch
        {
            _pristine = false;
        }
    }

    // The WPF WebView2 only comes to life once its window has been shown at
    // least once: measured, a never-shown window (EnsureHandle alone) still
    // hadn't initialized it after 15 s. So the very first preheat shows the
    // window once — off-screen, unactivated, out of the taskbar — and hides
    // it in the same breath. Measured then: core ready in 0.8 s and the page
    // loading while hidden, timers unthrottled.
    private void RealizeOffScreen()
    {
        if (_realized) return;
        _realized = true;
        if (IsVisible) return;

        var centered = WindowStartupLocation == WindowStartupLocation.CenterScreen;
        var (left, top, inTaskbar) = (Left, Top, ShowInTaskbar);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        ShowActivated = false;
        ShowInTaskbar = false;
        Show();
        Hide();
        ShowActivated = true;
        ShowInTaskbar = inTaskbar;

        // WindowStartupLocation only applies to the first Show, just spent
        // off-screen: place the window for the real one ourselves.
        if (centered)
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + (area.Height - Height) / 2;
        }
        else
        {
            Left = left;
            Top = top;
        }
    }

    // A blank temporary conversation, ready as soon as its composer exists —
    // not at the page's onload, which waits for every analytics script. The
    // cookie banner is refused on the way (once per profile: it is stored).
    private async Task<bool> PrepareConversationAsync()
    {
        var temporary = _temporaryChat();
        _preparedTemporary = temporary;
        await NewDocumentAsync(() =>
        {
            WebView.CoreWebView2.Navigate(ChatGptPage.NewConversationUrl(temporary));
            return Task.FromResult(true);
        }, NavigationTimeoutMs);
        if (!await WaitForComposerAsync()) return false;

        // Refusing posts a form: the page reloads, wait for the new composer.
        if (await NewDocumentAsync(() => ExecuteBoolAsync(ChatGptPage.RejectCookiesScript), ConsentReloadTimeoutMs))
            return await WaitForComposerAsync();
        return true;
    }

    // ── Send ──

    // image: the screenshot PNG, possibly still encoding — awaited only once
    // the page is ready, so encoding overlaps the page load. submit false:
    // the message is left in the composer, the user presses Enter.
    internal async Task SendAsync(string message, Task<byte[]?>? image, bool submit)
    {
        if (_sending) return;
        _sending = true;
        var watch = Stopwatch.StartNew();
        try
        {
            ShowStatus(StatusKind.Working, Loc.T("chat.opening"));
            // Shown BEFORE awaiting the preheat: should the WebView still be
            // waiting for a visible host, this is what lets it finish.
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();

            if (_preheat is { } preheat) await preheat;
            _preheat = null;
            await EnsureInitializedAsync();

            if ((!_pristine || _preparedTemporary != _temporaryChat()) && !await PrepareConversationAsync())
            {
                ShowStatus(StatusKind.Error, Loc.T("chat.noComposer"));
                return;
            }
            _pristine = false;

            var png = image is null ? null : await image;
            ShowStatus(StatusKind.Working, Loc.T(png is null ? "chat.sendingText" : "chat.sendingBoth"));
            WebView.Focus();

            var id = Guid.NewGuid().ToString("N");
            var result = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = result;
            try
            {
                await WebView.CoreWebView2.ExecuteScriptAsync(
                    ChatGptPage.SendScript(id, message, png is null ? null : Convert.ToBase64String(png), submit));
                var finished = await Task.WhenAny(result.Task, Task.Delay(SendResultTimeoutMs));
                if (finished != result.Task)
                {
                    ShowStatus(StatusKind.Error, Loc.T("chat.timeout"));
                    return;
                }
                Report(result.Task.Result, watch.Elapsed, submit);
            }
            finally
            {
                _pending.Remove(id);
            }
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _initialization = null;
            ShowStatus(StatusKind.Error, Loc.T("chat.noRuntime"));
        }
        catch (Exception ex)
        {
            if (_initialization is { IsFaulted: true }) _initialization = null;
            ShowStatus(StatusKind.Error, Loc.T("chat.failed", ex.Message));
        }
        finally
        {
            _sending = false;
        }
    }

    // ── Open, nothing sent ──

    // The Prompt Builder's ChatGPT button. The page is left as it is — the
    // preheated blank conversation, or the last answer — unless it was never
    // loaded, or is blank but in the other mode (temporary or not).
    internal async Task OpenAsync()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (_sending) return;

        try
        {
            if (_preheat is { IsCompleted: false } running) await running;
            // Here rather than in the preheat, which swallows its errors: a
            // missing WebView2 runtime must be said.
            await EnsureInitializedAsync();
            if (_preparedTemporary is null || (_pristine && _preparedTemporary != _temporaryChat()))
            {
                ShowStatus(StatusKind.Working, Loc.T("chat.opening"));
                _preheat = PreheatAsync();
                await _preheat;
                if (!_pristine)
                {
                    ShowStatus(StatusKind.Error, Loc.T("chat.noComposer"));
                    return;
                }
                StatusBar.Visibility = Visibility.Collapsed;
            }
            WebView.Focus();
            // From now on the user may type or send in it: the next send
            // starts its own conversation.
            _pristine = false;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _initialization = null;
            ShowStatus(StatusKind.Error, Loc.T("chat.noRuntime"));
        }
        catch (Exception ex)
        {
            if (_initialization is { IsFaulted: true }) _initialization = null;
            ShowStatus(StatusKind.Error, Loc.T("chat.failed", ex.Message));
        }
    }

    private void Report(JsonElement result, TimeSpan elapsed, bool submitted)
    {
        var ok = result.TryGetProperty("ok", out var okValue) && okValue.ValueKind == JsonValueKind.True;
        var at = result.TryGetProperty("at", out var atValue) ? atValue.GetString() : "";
        var imageMissed = result.TryGetProperty("imageAttached", out var image) && image.ValueKind == JsonValueKind.False;
        // Measured from the click on Envoyer to the click on ChatGPT's send
        // button: the latency this window is accountable for.
        var took = elapsed.TotalSeconds.ToString("0.0", Loc.Culture) + " s";

        if (ok && imageMissed)
            ShowStatus(StatusKind.Warning, Loc.T(submitted ? "chat.sentNoImage" : "chat.insertedNoImage", took));
        else if (ok && submitted)
            ShowStatus(StatusKind.Success, Loc.T("chat.sent", took));
        else if (ok)
            ShowStatus(StatusKind.Ready, Loc.T("chat.inserted", took));
        else
            ShowStatus(StatusKind.Error, at switch
            {
                "prompt" => Loc.T("chat.noComposer"),
                "send" => Loc.T("chat.sendNeverReady"),
                "navigated" => Loc.T("chat.navigated"),
                _ => Loc.T("chat.pageError", (result.TryGetProperty("detail", out var detail) ? detail.GetString() : at) ?? ""),
            });
    }

    // ── WebView plumbing ──

    private Task EnsureInitializedAsync() => _initialization ??= InitializeAsync();

    private async Task InitializeAsync()
    {
        var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = BrowserArguments };
        var environment = await CoreWebView2Environment.CreateAsync(null, _userDataFolder, options);
        await WebView.EnsureCoreWebView2Async(environment);

        var core = WebView.CoreWebView2;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsStatusBarEnabled = false;
        // The login survives restarts (its cookie is in the profile). Should
        // the session end anyway, the login form fills itself back from
        // Edge's password store — offered once at login, encrypted for the
        // Windows user, never handled by this app. Off by default in WebView2.
        core.Settings.IsPasswordAutosaveEnabled = true;
        core.Settings.IsGeneralAutofillEnabled = true;
        core.WebMessageReceived += OnWebMessage;
        core.NavigationStarting += OnNavigationStarting;
        // Links ChatGPT opens in a new tab (sources, citations) go to the
        // system browser.
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            try { Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true }); } catch { }
        };
    }

    // Runs `trigger`; when it reports having started a navigation, waits for
    // the NEW document to begin loading (ContentLoading). From then on, every
    // probe runs in that document, never in the one being replaced.
    //
    // Whether the page is usable is decided by WaitForComposerAsync, on what
    // the page shows: chatgpt.com redirects itself, and the first navigation
    // often ends in ERR_ABORTED.
    private async Task<bool> NewDocumentAsync(Func<Task<bool>> trigger, int timeoutMs)
    {
        var core = WebView.CoreWebView2;
        var loading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnContentLoading(object? sender, CoreWebView2ContentLoadingEventArgs e) => loading.TrySetResult();

        core.ContentLoading += OnContentLoading;
        try
        {
            if (!await trigger()) return false;
            return await Task.WhenAny(loading.Task, Task.Delay(timeoutMs)) == loading.Task;
        }
        finally
        {
            core.ContentLoading -= OnContentLoading;
        }
    }

    // Polled from here, one short synchronous script at a time (~1 ms each),
    // rather than awaited inside the page: a document replaced by a redirect
    // would take an in-page wait down with it.
    private async Task<bool> WaitForComposerAsync()
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < PageReadyTimeoutMs)
        {
            try
            {
                if (await ExecuteBoolAsync(ChatGptPage.ReadyProbeScript)) return true;
            }
            catch { /* document swapped mid-call: try again */ }
            await Task.Delay(ComposerPollMs);
        }
        return false;
    }

    private async Task<bool> ExecuteBoolAsync(string script) =>
        await WebView.CoreWebView2.ExecuteScriptAsync(script) == "true";

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && _pending.TryGetValue(id.GetString()!, out var waiting))
            {
                waiting.TrySetResult(root.Clone());
            }
        }
        catch { /* not one of ours */ }
    }

    // A page that navigates while the send script runs takes the script with
    // it, and its report never comes. Give a just-posted report a moment to
    // arrive (the script reports right before clicking Send, which may itself
    // navigate), then stop waiting.
    private async void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_pending.Count == 0) return;
        var waiting = _pending.Values.ToList();
        await Task.Delay(1500);
        foreach (var result in waiting)
            result.TrySetResult(NavigatedAway);
    }

    private static readonly JsonElement NavigatedAway =
        JsonDocument.Parse("""{"ok":false,"at":"navigated"}""").RootElement.Clone();

    // ── Status strip ──

    // Ready: the message waits in the composer for the user's Enter.
    private enum StatusKind { Working, Ready, Success, Warning, Error }

    private void ShowStatus(StatusKind kind, string text)
    {
        _statusHide.Stop();
        var (glyph, color) = kind switch
        {
            StatusKind.Working => ("", Color.FromRgb(0x93, 0xC5, 0xFD)),
            StatusKind.Ready => ("", Color.FromRgb(0x93, 0xC5, 0xFD)),
            StatusKind.Success => ("", Color.FromRgb(0x4A, 0xDE, 0x80)),
            StatusKind.Warning => ("", Color.FromRgb(0xFA, 0xCC, 0x15)),
            _ => ("", Color.FromRgb(0xF4, 0x71, 0x74)),
        };
        StatusGlyph.Text = glyph;
        StatusGlyph.Foreground = new SolidColorBrush(color);
        StatusText.Text = text;
        StatusBar.Visibility = Visibility.Visible;
        if (kind == StatusKind.Success) _statusHide.Start();
    }

    // ── Window lifetime ──

    // The answer stays here; the Prompt Builder comes back in front, as it
    // was left, to adjust the prompt or the text and send again.
    private void Back_Click(object sender, RoutedEventArgs e) => _backToPromptBuilder();

    // Closing only hides — and prepares the next conversation right away,
    // while nobody is looking.
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _saveBounds(WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds);
        if (_closingForGood) return;
        e.Cancel = true;
        Hide();
        // Deferred: at app exit this Closing runs too (with the Cancel
        // ignored), and the queued preheat then never runs on a dead window.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            if (PresentationSource.FromVisual(this) is not null) Preheat();
        });
    }

    // At app exit WPF has usually closed every window already (Shutdown
    // ignores our Cancel); what is left is releasing the browser processes.
    internal void CloseForGood()
    {
        _closingForGood = true;
        try { Close(); } catch (InvalidOperationException) { }
        WebView.Dispose();
    }

    private static bool IsOnScreen(Rect bounds)
    {
        var desktop = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return bounds.Width > 0 && bounds.Height > 0 && desktop.IntersectsWith(bounds);
    }

    // Dark caption to match the page (Windows 10 2004+ / 11), square corners
    // on Windows 11 like everything else in the app.
    private void ConfigureNativeWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int enabled = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
        int square = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref square, sizeof(int));
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DONOTROUND = 1;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
