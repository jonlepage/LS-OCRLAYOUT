using System.IO;

namespace ScreenSearchOverlay;

// What the ChatGPT window did, one line per step, in chat.log next to the
// exe: a send that fails on a machine nobody can watch leaves a trace.
// Never the message, never an address — steps, timings and the page's
// reports only. Past 256 Ko the file starts over.
//
// Written from the UI thread only; the disk writes are chained on the
// thread pool, so they keep their order and never slow a send down.
internal static class ChatLog
{
    private const long MaxBytes = 256 * 1024;

    private static string? _path;
    private static Task _tail = Task.CompletedTask;

    internal static void Start(string path) => _path = path;

    internal static void Write(string line)
    {
        if (_path is not { } path) return;
        var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {line}{Environment.NewLine}";
        _tail = _tail.ContinueWith(_ =>
        {
            try
            {
                if (new FileInfo(path) is { Exists: true, Length: > MaxBytes }) File.Delete(path);
                File.AppendAllText(path, text);
            }
            catch { /* a log must never break what it watches */ }
        }, TaskScheduler.Default);
    }
}
