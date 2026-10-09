using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ScreenSearchOverlay;

internal enum UpdateStage { Idle, Checking, UpToDate, Available, Downloading, Failed }

// Updates: checked at startup, at most once a day — the app lives in the
// tray for days, and may be restarted many times in one — and on demand from
// the tray menu or the Prompt Builder's settings. A new version is offered by
// a tray balloon and the Prompt Builder's title bar; installing downloads it,
// swaps the exe and restarts (see Updater).
public partial class App
{
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromDays(1);
    // How often the tray-resident app looks whether a day has passed.
    private static readonly TimeSpan UpdateCheckTick = TimeSpan.FromHours(1);

    private DispatcherTimer? _updateTimer;
    private Forms.ToolStripItem? _trayUpdate;
    // A version is offered once per run by the automatic checks.
    private Version? _offeredVersion;
    // Balloons also say "up to date" or "updated": only an offer installs on click.
    private bool _balloonOffersUpdate;
    private double _updateProgress;
    private string _updateError = "";

    internal bool CheckUpdatesAutomatically { get; set; } = true;
    // The last check that found nothing newer (UTC), saved. A check that found
    // a version is not recorded: the next start asks again, so the offer
    // shows right away instead of a day later.
    internal DateTime? LastUpdateCheck { get; set; }
    internal UpdateStage UpdateStage { get; private set; }
    internal UpdateInfo? AvailableUpdate { get; private set; }

    // The tray menu and the Prompt Builder's settings follow it.
    internal event Action? UpdateChanged;

    private void SetupUpdates(bool afterUpdate)
    {
        _trayIcon!.BalloonTipClicked += (_, _) =>
        {
            if (_balloonOffersUpdate) InstallUpdate(confirm: true);
        };
        UpdateChanged += RefreshTrayUpdate;
        if (afterUpdate) ShowBalloon(Loc.T("update.installed", Updater.Current));

        // Release builds only (build.ps1): a debug build has no release to
        // compare with, and must not replace itself with one.
#if !DEBUG
        // At startup as soon as the app is idle — the request is async, it
        // costs nothing to wait for — then whenever a day has passed.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, CheckForUpdatesIfDue);
        _updateTimer = new DispatcherTimer { Interval = UpdateCheckTick };
        _updateTimer.Tick += (_, _) => CheckForUpdatesIfDue();
        _updateTimer.Start();
#endif
    }

    private void CheckForUpdatesIfDue()
    {
        if (!CheckUpdatesAutomatically) return;
        if (LastUpdateCheck is { } last && DateTime.UtcNow - last < UpdateCheckInterval) return;
        CheckForUpdates(manual: false);
    }

    private void TeardownUpdates() => _updateTimer?.Stop();

    // ── What the update row says, in the tray and in the settings ──

    internal string UpdateStatusText => UpdateStage switch
    {
        UpdateStage.Checking => Loc.T("update.checking"),
        UpdateStage.UpToDate => Loc.T("update.upToDate", Updater.Current),
        UpdateStage.Available => Loc.T("update.available", AvailableUpdate!.Version, Updater.Current),
        UpdateStage.Downloading => Loc.T("update.downloading", _updateProgress.ToString("P0", Loc.Culture)),
        UpdateStage.Failed => _updateError,
        _ => Loc.T("update.current", Updater.Current),
    };

    internal string UpdateActionText => UpdateStage == UpdateStage.Available
        ? Loc.T("update.install", AvailableUpdate!.Version)
        : Loc.T("update.check");

    internal bool UpdateBusy => UpdateStage is UpdateStage.Checking or UpdateStage.Downloading;

    // The row's button: install what was found, or look. From the tray the
    // answer comes as a balloon, there is nothing else to show it.
    internal void RunUpdateAction(bool fromTray = false)
    {
        if (UpdateStage == UpdateStage.Available) InstallUpdate(confirm: false);
        else CheckForUpdates(manual: true, balloon: fromTray);
    }

    private void RefreshTrayUpdate()
    {
        if (_trayUpdate is null) return;
        _trayUpdate.Text = UpdateBusy ? UpdateStatusText : UpdateActionText;
        _trayUpdate.Enabled = !UpdateBusy;
    }

    // ── Check, download, install ──

    private async void CheckForUpdates(bool manual, bool balloon = false)
    {
        if (UpdateBusy) return;
        SetUpdateStage(UpdateStage.Checking);
        try
        {
            AvailableUpdate = await Updater.CheckAsync();
        }
        catch (Exception ex)
        {
            AvailableUpdate = null;
            _updateError = Loc.T("update.checkFailed", ex.Message);
            // Offline at startup is no news: only a check asked for reports it.
            SetUpdateStage(manual ? UpdateStage.Failed : UpdateStage.Idle);
            if (balloon) ShowBalloon(_updateError);
            return;
        }

        if (AvailableUpdate is not { } update)
        {
            LastUpdateCheck = DateTime.UtcNow;
            SaveSettings();
            SetUpdateStage(UpdateStage.UpToDate);
            if (balloon) ShowBalloon(Loc.T("update.upToDateBalloon", Updater.Current));
            return;
        }

        SetUpdateStage(UpdateStage.Available);
        if (manual && !balloon) return; // the settings row already says it
        if (!manual && update.Version == _offeredVersion) return;
        _offeredVersion = update.Version;
        ShowBalloon(Loc.T("update.offer", update.Version), Loc.T("update.offer.title"), offersUpdate: true);
    }

    // confirm: from the balloon, which only offers. The settings and tray
    // buttons already say "install and restart".
    private async void InstallUpdate(bool confirm)
    {
        if (AvailableUpdate is not { } update || UpdateBusy) return;
        if (confirm && Forms.MessageBox.Show(
                Loc.T("update.confirm", update.Version, (update.Size / 1048576.0).ToString("0", Loc.Culture)),
                "ScreenSearchOverlay",
                Forms.MessageBoxButtons.YesNo,
                Forms.MessageBoxIcon.Question) != Forms.DialogResult.Yes)
            return;

        _updateProgress = 0;
        SetUpdateStage(UpdateStage.Downloading);
        try
        {
            // Created here, on the UI thread: reports land back on it.
            var progress = new Progress<double>(p =>
            {
                _updateProgress = p;
                UpdateChanged?.Invoke();
            });
            // ~200 MB written and hashed: off the UI thread.
            var file = await Task.Run(() => Updater.DownloadAsync(update, progress));
            Updater.Install(file);
        }
        catch (Exception ex)
        {
            var reason = ex switch
            {
                InvalidDataException => Loc.T("update.corrupt"),
                UnauthorizedAccessException => Loc.T("update.noWrite", Path.GetDirectoryName(Environment.ProcessPath) ?? ""),
                _ => ex.Message,
            };
            // Still available: the offer (title bar pill, settings) comes
            // back for another try. The box says why this one failed.
            SetUpdateStage(UpdateStage.Available);

            // A folder we cannot write to (Program Files…) won't get better
            // by retrying: the release page is the way out.
            if (Forms.MessageBox.Show(
                    Loc.T("update.failed", reason) + "\n\n" + Loc.T("update.openPage"),
                    "ScreenSearchOverlay",
                    Forms.MessageBoxButtons.YesNo,
                    Forms.MessageBoxIcon.Warning) == Forms.DialogResult.Yes)
            {
                try { Process.Start(new ProcessStartInfo(Updater.ReleasesPageUrl) { UseShellExecute = true }); } catch { }
            }
            return;
        }

        // The new version is starting; it waits for this one to exit.
        Shutdown();
    }

    private void SetUpdateStage(UpdateStage stage)
    {
        UpdateStage = stage;
        UpdateChanged?.Invoke();
    }

    private void ShowBalloon(string text, string? title = null, bool offersUpdate = false)
    {
        if (_trayIcon is null) return;
        _balloonOffersUpdate = offersUpdate;
        _trayIcon.ShowBalloonTip(10_000, title ?? "ScreenSearchOverlay", text, Forms.ToolTipIcon.Info);
    }
}
