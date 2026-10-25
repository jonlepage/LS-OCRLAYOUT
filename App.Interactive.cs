namespace ScreenSearchOverlay;

// The interactive guide (MCP guide_step, end_guide): the agent walks the
// user through a path of clicks (Edit > Preferences… > Interface…) on the
// live app. Each step rings its target over the real screen (GuideOverlay,
// click-through) and the tool call waits for the user's click, seen by a
// mouse hook that lets it through. Once the app has reacted — a menu opens,
// a window appears — the call answers with a fresh capture of the app's
// frontmost window, for the next step. Esc (a global hotkey while the guide
// runs) or the panel's ✕ leaves it, and the waiting call says so.
public partial class App
{
    private const int GUIDE_ESC_HOTKEY_ID = 9002;
    private const uint VK_ESCAPE = 0x1B;
    // Past it the call answers "no click yet" and the agent calls again.
    private static readonly TimeSpan GuideStepWait = TimeSpan.FromSeconds(McpServer.GuideStepWaitSeconds);
    // The app reacts to the click (a menu opens, a window appears) before
    // the capture.
    private const int GuideClickSettleMs = 700;
    // On systems that can't leave the overlay out of captures, it hides
    // this long around one.
    private const int GuideHideMs = 60;
    // "Done" stays on screen this long after end_guide.
    private const int GuideFinishMs = 1500;

    private GuideOverlay? _interactive;
    private IReadOnlyList<string> _guideSteps = [];
    private LowLevelMouseHook? _guideHook;
    private bool _guideEscRegistered;
    // The step waiting for its click, and where clicks count: the captured
    // window (a click in the chat or another app is not an answer).
    private TaskCompletionSource<(GuideStepOutcome Outcome, System.Drawing.Point At)>? _guideWait;
    private int _guideWaitStep;
    private Rectangle _guideArea;

    Task<GuideStepResult> IScreenGuide.StepAsync(IReadOnlyList<string> steps, int current, GuideSpot spot) =>
        Dispatcher.InvokeAsync(() => StepAsync(steps, current, spot)).Task.Unwrap();

    Task IScreenGuide.EndGuideAsync(string? message) =>
        Dispatcher.InvokeAsync(() => EndGuide(message)).Task;

    private async Task<GuideStepResult> StepAsync(IReadOnlyList<string> steps, int current, GuideSpot spot)
    {
        if (_guideCapture is not { } capture)
            return new(GuideStepOutcome.Failed, Error: GuideNoLook);

        TaskCompletionSource<(GuideStepOutcome Outcome, System.Drawing.Point At)> wait;
        // Clicked after the previous call for this step gave up waiting
        if (_guideWait is { Task.IsCompletedSuccessfully: true } late && _guideWaitStep == current
            && late.Task.Result.Outcome is GuideStepOutcome.Clicked or GuideStepOutcome.ClickedElsewhere)
            wait = late;
        else
        {
            if (!capture.Contains(spot.X, spot.Y))
                return new(GuideStepOutcome.Failed, Error: GuideOutside(capture));
            // A frozen show_me / explain_screen picture would cover the app
            CloseOverlay();
            _guideWait?.TrySetResult((GuideStepOutcome.Replaced, default));
            // The guide follows the app to another monitor
            if (_interactive is { } moved && moved.Screen != capture.Info)
            {
                moved.Close();
                _interactive = null;
            }
            if (_interactive is null)
            {
                try
                {
                    _interactive = StartInteractiveGuide(capture.Info);
                }
                catch (Exception ex)
                {
                    StopInteractiveGuide(GuideStepOutcome.Failed);
                    return new(GuideStepOutcome.Failed, Error: $"Can't watch the user's clicks ({ex.Message}): use show_me instead.");
                }
            }
            _guideSteps = steps;
            _guideArea = capture.Area;
            _guideWaitStep = current;
            wait = _guideWait = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _interactive.ShowStep(steps, current, capture.Mark(spot));
        }

        if (await Task.WhenAny(wait.Task, Task.Delay(GuideStepWait)) != wait.Task)
            return new(GuideStepOutcome.NoClick);
        var (outcome, at) = wait.Task.Result;
        if (outcome is not (GuideStepOutcome.Clicked or GuideStepOutcome.ClickedElsewhere) || _interactive is not { } guide)
            return new(outcome);
        _guideWait = null;

        guide.StepTaken(steps, current);
        await Task.Delay(GuideClickSettleMs);
        if (_interactive != guide)
            return new(GuideStepOutcome.Left);

        // The app's frontmost window now: the menu that opened, the dialog
        // that appeared. Never brought to the front: that closes open menus.
        var target = capture.ProcessId is { } pid ? TopWindows.List().FirstOrDefault(w => w.ProcessId == pid) : null;
        if (!guide.ExcludedFromCapture)
        {
            guide.SetVisible(false);
            await Task.Delay(GuideHideMs);
        }
        var look = await CaptureForAgentAsync(target, grid: true);
        guide.SetVisible(true);
        return new(outcome, look, _guideCapture!.ImagePoint(at));
    }

    private GuideOverlay StartInteractiveGuide(ScreenInfo screen)
    {
        if (_guideHook is null)
        {
            var hook = new LowLevelMouseHook();
            hook.ButtonDown += OnGuideClick;
            hook.Install();
            _guideHook = hook;
        }
        // Esc leaves the guide, whichever app has the keyboard. Taken by
        // another app: the panel's ✕ still does.
        if (!_guideEscRegistered && _hwndSource is { } host)
            _guideEscRegistered = RegisterHotKey(host.Handle, GUIDE_ESC_HOTKEY_ID, MOD_NOREPEAT, VK_ESCAPE);
        return new GuideOverlay(screen, () => StopInteractiveGuide(GuideStepOutcome.Left));
    }

    private void OnGuideClick(int x, int y)
    {
        if (_guideWait is not { Task.IsCompleted: false } wait || _interactive is not { } guide) return;
        // The panel takes its own clicks
        if (guide.IsOnPanel(x, y) || !_guideArea.Contains(x, y)) return;
        var outcome = guide.HitsTarget(x, y) ? GuideStepOutcome.Clicked : GuideStepOutcome.ClickedElsewhere;
        wait.TrySetResult((outcome, new System.Drawing.Point(x, y)));
    }

    // Every step done, the agent's last words, then gone
    private void EndGuide(string? message)
    {
        if (_interactive is not { } guide) return;
        _guideWait?.TrySetResult((GuideStepOutcome.Replaced, default));
        _guideWait = null;
        guide.Finish(_guideSteps, message);
        _ = CloseGuideLaterAsync(guide);
    }

    private async Task CloseGuideLaterAsync(GuideOverlay guide)
    {
        await Task.Delay(GuideFinishMs);
        // Not a newer guide started meanwhile
        if (_interactive == guide)
            StopInteractiveGuide(GuideStepOutcome.Replaced);
    }

    // The waiting call, if any, answers with outcome
    private void StopInteractiveGuide(GuideStepOutcome outcome)
    {
        _guideWait?.TrySetResult((outcome, default));
        _guideWait = null;
        _interactive?.Close();
        _interactive = null;
        _guideHook?.Dispose();
        _guideHook = null;
        if (_guideEscRegistered && _hwndSource is { } host)
            UnregisterHotKey(host.Handle, GUIDE_ESC_HOTKEY_ID);
        _guideEscRegistered = false;
    }
}
