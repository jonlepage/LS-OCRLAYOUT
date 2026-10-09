using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace ScreenSearchOverlay;

// A newer release. Sha256: GitHub's own digest of the asset, absent on
// assets uploaded before GitHub computed them.
internal sealed record UpdateInfo(Version Version, string DownloadUrl, long Size, string? Sha256);

// Self-update from the repository's GitHub releases — no UI dependency.
// build.ps1 -Release publishes the exe as the asset of a "vX.Y.Z" release;
// this finds the latest one, downloads it next to the running exe and swaps
// the two files.
//
// Windows won't overwrite or delete a running exe, but it lets one be
// renamed: the running exe becomes "<exe>.old", the download takes its name,
// the new version starts, waits for this one to exit (it holds the hotkeys),
// and deletes the .old.
internal static class Updater
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/jonlepage/LS-OCRLAYOUT/releases/latest";
    internal const string ReleasesPageUrl = "https://github.com/jonlepage/LS-OCRLAYOUT/releases/latest";
    private const string AssetName = "ScreenSearchOverlay.exe";
    private const string AfterUpdateArgument = "--after-update";
    private const int CheckTimeoutMs = 20_000;
    private const int PreviousExitTimeoutMs = 15_000;

    // The exe's, i.e. package.json's: three numbers, like the release tags.
    // Declared before Http, whose User-Agent reads it: static fields are
    // initialized in declaration order.
    internal static Version Current { get; } = ThreeParts(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0));

    // No overall timeout: the exe is ~200 MB, a slow link needs minutes.
    // The check has its own (CheckTimeoutMs). Created on first use, so
    // FinishUpdate at startup never pays for it.
    private static readonly Lazy<HttpClient> HttpClientInstance = new(CreateClient);
    private static HttpClient Http => HttpClientInstance.Value;

    private static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException("No process path");

    // The latest release when it is newer than this exe and carries the exe;
    // null otherwise. Drafts and prereleases are never "latest".
    internal static async Task<UpdateInfo?> CheckAsync()
    {
        using var timeout = new CancellationTokenSource(CheckTimeoutMs);
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Http.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        var release = document.RootElement;

        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var parsed)) return null;
        var version = ThreeParts(parsed);
        if (version <= Current) return null;

        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
            var digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            return new UpdateInfo(
                version,
                asset.GetProperty("browser_download_url").GetString()!,
                asset.GetProperty("size").GetInt64(),
                digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null);
        }
        // Release published, exe not uploaded yet: next check.
        return null;
    }

    // Next to the running exe — same volume, so the swap is a rename — then
    // checked against the size and digest GitHub announced. progress: 0…1,
    // reported once per percent.
    internal static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double> progress)
    {
        var target = ExePath + ".download";
        using var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long received = 0;
        var percent = -1;
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read));
                    hash.AppendData(buffer, 0, read);
                    received += read;
                    var now = (int)(received * 100 / Math.Max(update.Size, 1));
                    if (now != percent) progress.Report((percent = now) / 100.0);
                }
            }

            if (received != update.Size
                || (update.Sha256 is { } expected
                    && !Convert.ToHexString(hash.GetHashAndReset()).Equals(expected, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Downloaded file does not match the release");
        }
        catch
        {
            TryDelete(target);
            throw;
        }
        return target;
    }

    // Swaps the files and starts the new version; the caller then exits.
    // Every step is undone if a later one fails: the app is never left
    // without its exe.
    internal static void Install(string downloaded)
    {
        var exe = ExePath;
        var old = exe + ".old";
        File.Move(exe, old, overwrite: true);
        try
        {
            File.Move(downloaded, exe);
            try
            {
                var start = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exe)!,
                };
                start.ArgumentList.Add(AfterUpdateArgument);
                start.ArgumentList.Add(Environment.ProcessId.ToString());
                Process.Start(start);
            }
            catch
            {
                File.Move(exe, downloaded, overwrite: true);
                throw;
            }
        }
        catch
        {
            File.Move(old, exe, overwrite: true);
            throw;
        }
    }

    // First thing at startup. Launched by Install: waits for the previous
    // version to exit — it still holds the hotkeys and the .old file. Then
    // clears what an update leaves behind. True when this run follows an update.
    internal static bool FinishUpdate(string[] args)
    {
        var index = Array.IndexOf(args, AfterUpdateArgument);
        var afterUpdate = index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out _);
        if (afterUpdate)
        {
            try
            {
                using var previous = Process.GetProcessById(int.Parse(args[index + 1]));
                previous.WaitForExit(PreviousExitTimeoutMs);
            }
            catch (ArgumentException) { /* already gone */ }
            catch (InvalidOperationException) { }
        }

        if (Environment.ProcessPath is { } exe)
        {
            TryDelete(exe + ".old");
            TryDelete(exe + ".download");
        }
        return afterUpdate;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        // GitHub's API refuses requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ScreenSearchOverlay/{Current}");
        return client;
    }

    private static Version ThreeParts(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
