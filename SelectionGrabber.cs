using System.Diagnostics;
using System.Runtime.InteropServices;
using Clipboard = System.Windows.Clipboard;

namespace ScreenSearchOverlay;

// Copies the selection of whatever app has the focus by sending it a real
// Ctrl+C — the only method that works everywhere (browsers, Office, Electron
// apps…). UI Automation's TextPattern would spare the clipboard, but only a
// fraction of apps implement it.
//
// The copied text stays in the clipboard (and in Win+V history) on purpose.
//
// UI-thread only: the clipboard needs an STA thread, and the awaits resume on
// the dispatcher.
internal static partial class SelectionGrabber
{
    // How long the target app gets to answer Ctrl+C. Apps that have a
    // selection answer in 10–50 ms; this bound is only paid when nothing is
    // selected (the clipboard then never changes).
    private const int CopyTimeoutMs = 300;
    // Keys we wait for the user to release rather than releasing them
    // ourselves: the hotkey's own letter, and the Windows key.
    private const int KeyReleaseTimeoutMs = 300;
    private const int PollMs = 10;

    // hotkeyKey: the letter of the hotkey that triggered the copy (G).
    internal static async Task<string> CopySelectionAsync(ushort hotkeyKey)
    {
        // The hotkey fires on key DOWN: Ctrl and Alt are still held, and
        // Ctrl+C sent now would reach the app as Ctrl+Alt+C.
        await ReleaseModifiersAsync(hotkeyKey);

        var before = GetClipboardSequenceNumber();
        SendCtrlC();

        // The sequence number changes as soon as the app writes the
        // clipboard. Unchanged = nothing was selected (or the app ignores
        // Ctrl+C): the previous clipboard content is NOT the selection.
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < CopyTimeoutMs)
        {
            await Task.Delay(PollMs);
            if (GetClipboardSequenceNumber() != before)
                return await ReadTextAsync();
        }
        return "";
    }

    // Read as soon as the clipboard changes. An app may still hold it open,
    // or have emptied it without having written the text yet: retry briefly
    // instead of sleeping a fixed time on every copy.
    private static async Task<string> ReadTextAsync()
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                if (Clipboard.ContainsText()) return Clipboard.GetText();
            }
            catch (COMException) { }
            await Task.Delay(PollMs);
        }
        return "";
    }

    // Ctrl, Alt and Shift are released on the app's side instead of waiting
    // for the fingers to leave them (100–250 ms on every press). The real
    // key-ups that follow find them already up and do nothing: Alt cannot
    // open a menu, a key went down in between.
    //
    // The hotkey's letter is waited for, though (~80 ms, it is released
    // first): held past the keyboard's repeat delay with Ctrl and Alt
    // already up for the system, its auto-repeat would type "g" into the
    // app. The Windows key too — a lone Win key-up opens the Start menu.
    private static readonly ushort[] Releasable = [VK_SHIFT, VK_CONTROL, VK_MENU];

    private static async Task ReleaseModifiersAsync(ushort hotkeyKey)
    {
        var watch = Stopwatch.StartNew();
        while ((IsDown(hotkeyKey) || IsDown(VK_LWIN) || IsDown(VK_RWIN)) && watch.ElapsedMilliseconds < KeyReleaseTimeoutMs)
            await Task.Delay(PollMs);

        var held = Releasable.Where(IsDown).Select(vk => Key(vk, up: true)).ToArray();
        if (held.Length > 0)
            SendInput((uint)held.Length, held, Marshal.SizeOf<INPUT>());
    }

    private static bool IsDown(ushort vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static void SendCtrlC()
    {
        INPUT[] inputs =
        [
            Key(VK_CONTROL, up: false),
            Key(VK_C, up: false),
            Key(VK_C, up: true),
            Key(VK_CONTROL, up: true),
        ];
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION
        {
            ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 }
        }
    };

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_C = 0x43;

    // INPUT must keep MOUSEINPUT in its union even though we only send keys:
    // it is the largest member, and SendInput rejects a cbSize that doesn't
    // match the real 40-byte (x64) structure.
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();
}
