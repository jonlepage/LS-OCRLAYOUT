namespace ScreenSearchOverlay;

// One instance per Windows session. A second one could not register the
// hotkeys, and both would write the same settings.json and prompts.json,
// each over the other's changes. Launching the exe again instead tells the
// running instance, which answers with a tray balloon.
public partial class App
{
    private const string InstanceMutexName = @"Local\LEPASOFT.ScreenSearchOverlay";
    private const string SecondLaunchEventName = @"Local\LEPASOFT.ScreenSearchOverlay.SecondLaunch";
    // After an update the previous version may still be closing.
    private const int AfterUpdateClaimTimeoutMs = 15_000;

    private Mutex? _instanceMutex;
    private EventWaitHandle? _secondLaunch;
    private RegisteredWaitHandle? _secondLaunchWait;

    // False: another instance runs; it was told, this one must exit.
    private bool ClaimSingleInstance(bool afterUpdate)
    {
        var mutex = new Mutex(false, InstanceMutexName);
        bool owned;
        try
        {
            owned = mutex.WaitOne(afterUpdate ? AfterUpdateClaimTimeoutMs : 0);
        }
        catch (AbandonedMutexException)
        {
            owned = true; // the previous instance was killed: ours now
        }

        if (!owned)
        {
            mutex.Dispose();
            if (EventWaitHandle.TryOpenExisting(SecondLaunchEventName, out var running))
            {
                running.Set();
                running.Dispose();
            }
            return false;
        }

        _instanceMutex = mutex;
        return true;
    }

    // Once the tray icon exists: it is what answers.
    private void ListenForSecondLaunch()
    {
        _secondLaunch = new EventWaitHandle(false, EventResetMode.AutoReset, SecondLaunchEventName);
        _secondLaunchWait = ThreadPool.RegisterWaitForSingleObject(_secondLaunch,
            (_, _) => Dispatcher.BeginInvoke(() => ShowBalloon(Loc.T("app.alreadyRunning"))),
            null, Timeout.Infinite, executeOnlyOnce: false);
    }

    private void ReleaseSingleInstance()
    {
        _secondLaunchWait?.Unregister(null);
        _secondLaunch?.Dispose();
        if (_instanceMutex is null) return;
        try { _instanceMutex.ReleaseMutex(); } catch (ApplicationException) { }
        _instanceMutex.Dispose();
    }
}
