using Microsoft.Win32;

namespace ScreenSearchOverlay;

// Start with Windows: an entry in the user's Run key, the list Windows
// launches at sign-in (Task Manager's Startup apps). Off until the user ticks
// it, in the Prompt Builder's settings or the tray menu.
//
// The registry is the only record, read at every look: Task Manager and
// Windows' settings can turn it off while the app runs. The entry is this
// exe's — another copy (dist/, a download) shows it off, and ticking it there
// moves the entry to that copy. An update keeps the exe's path, so the entry.
public partial class App
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Task Manager's own switch, undocumented: first byte 02 on, 03 off.
    private const string StartupApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string StartupValueName = "ScreenSearchOverlay";

    // Quoted: the path may hold spaces.
    private static string? StartupCommand =>
        Environment.ProcessPath is { } exe ? $"\"{exe}\"" : null;

    internal bool StartsWithWindows
    {
        get
        {
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                if (StartupCommand is not { } command
                    || run?.GetValue(StartupValueName) is not string registered
                    || !string.Equals(registered, command, StringComparison.OrdinalIgnoreCase))
                    return false;
                using var approved = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath);
                return approved?.GetValue(StartupValueName) is not byte[] { Length: > 0 } state
                       || (state[0] & 1) == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    // On: the entry, and Task Manager's switch cleared — ticking it here is
    // turning it back on. Off: both removed, nothing left behind. A write that
    // fails changes nothing: the checkboxes read the registry back.
    internal void SetStartWithWindows(bool enabled)
    {
        if (StartupCommand is not { } command) return;
        try
        {
            using (var approved = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, writable: true))
                approved?.DeleteValue(StartupValueName, throwOnMissingValue: false);
            using var run = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled) run.SetValue(StartupValueName, command);
            else run.DeleteValue(StartupValueName, throwOnMissingValue: false);
        }
        catch { }
    }
}
