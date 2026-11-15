using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Perch;

public partial class Settings
{
    public bool UpdateCheck { get; set; }            // look for a newer release once a day. Off until you switch it on
    public string UpdateLastCheck { get; set; } = "";
    public string UpdateSkipped { get; set; } = "";  // a version you said no to
}

// Updates from the project's GitHub releases. The only thing sent is the request for the latest release, with no information about you or this PC.
// A download is checked against the checksum published with it before anything is replaced, and the old version is kept for rolling back.
public static class Updater
{
    public const string Repo = "rayanno0sarif-ops/perch";
    public record Info(string Version, string Notes, string ZipUrl, string ShaUrl, string Page);
    public static Info? Available { get; private set; }
    static readonly HttpClient Http = CreateClient();
    static HttpClient CreateClient() { var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) }; c.DefaultRequestHeaders.UserAgent.ParseAdd("Perch-updater"); c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json"); return c; }

    public static Version Current => System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Math.Max(0, Current.Build)}";
    static string BackupDir => Path.Combine(Settings.Dir, "previous");

    // Returns a message for the settings page. A newer release is kept in Available.
    public static async Task<string> Check(bool manual)
    {
        try
        {
            var json = await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            using var doc = JsonDocument.Parse(json); var r = doc.RootElement;
            string tag = r.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return "The latest release has a version Perch cannot read.";
            Settings.Current.UpdateLastCheck = DateTime.Now.ToString("g"); Settings.Current.Save();
            if (v <= new Version(Current.Major, Current.Minor, Math.Max(0, Current.Build))) { Available = null; return "You are on the latest version (" + CurrentText + ")."; }
            string zip = "", sha = "";
            foreach (var a in r.GetProperty("assets").EnumerateArray())
            {
                string n = a.GetProperty("name").GetString() ?? "", url = a.GetProperty("browser_download_url").GetString() ?? "";
                if (n.Equals("Perch-win-x64.zip", StringComparison.OrdinalIgnoreCase)) zip = url; else if (n.Equals("Perch-win-x64.zip.sha256", StringComparison.OrdinalIgnoreCase)) sha = url;
            }
            if (zip.Length == 0 || sha.Length == 0) return "Version " + v + " exists, but it has no Windows download with a checksum yet.";
            var info = new Info(v.ToString(), r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "", zip, sha, r.GetProperty("html_url").GetString() ?? "");
            Available = info;
            if (!manual && Settings.Current.UpdateSkipped == info.Version) return "";
            return "Version " + info.Version + " is ready.";
        }
        catch (Exception e) { return manual ? "Could not check: " + (e.Message.Length > 80 ? e.Message[..80] : e.Message) : ""; }
    }

    // Downloads, verifies and swaps in the new version, then restarts Perch. Returns an error message, or "" when it is restarting.
    public static async Task<string> Install(Action<string> progress)
    {
        var info = Available; if (info == null) return "There is no update to install.";
        try
        {
            string work = Path.Combine(Path.GetTempPath(), "perch-update-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(work);
            string zipPath = Path.Combine(work, "update.zip");
            progress("Downloading " + info.Version + "…");
            await using (var s = await Http.GetStreamAsync(info.ZipUrl)) await using (var f = File.Create(zipPath)) await s.CopyToAsync(f);
            progress("Checking the download…");
            string expected = (await Http.GetStringAsync(info.ShaUrl)).Trim().Split(' ', '\t', '\n')[0].ToLowerInvariant();
            string actual; await using (var f = File.OpenRead(zipPath)) actual = Convert.ToHexString(await SHA256.HashDataAsync(f)).ToLowerInvariant();
            if (expected != actual) { try { Directory.Delete(work, true); } catch { } return "The download did not match its checksum, so nothing was changed."; }
            progress("Unpacking…");
            string fresh = Path.Combine(work, "new"); ZipFile.ExtractToDirectory(zipPath, fresh);
            if (!File.Exists(Path.Combine(fresh, "Perch.exe"))) return "The download does not contain Perch.exe, so nothing was changed.";
            string dst = AppContext.BaseDirectory.TrimEnd('\\', '/'), exe = Environment.ProcessPath!;
            string backup = Path.Combine(BackupDir, CurrentText);
            // a PowerShell helper waits for Perch to quit, keeps the old files, copies the new ones over and starts Perch again
            string script = Path.Combine(work, "update.ps1");
            File.WriteAllText(script, string.Join("\r\n",
                "param($procId,$src,$dst,$backup,$exe)",
                "Wait-Process -Id $procId -ErrorAction SilentlyContinue",
                "Start-Sleep -Seconds 1",
                "New-Item -ItemType Directory -Force $backup | Out-Null",
                "robocopy $dst $backup /E /NFL /NDL /NJH /NJS | Out-Null",
                "robocopy $src $dst /E /NFL /NDL /NJH /NJS | Out-Null",
                "Start-Process -FilePath $exe"));
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" {Environment.ProcessId} \"{fresh}\" \"{dst}\" \"{backup}\" \"{exe}\"") { UseShellExecute = false, CreateNoWindow = true });
            progress("Restarting…");
            System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
            return "";
        }
        catch (Exception e) { return "The update failed: " + (e.Message.Length > 90 ? e.Message[..90] : e.Message); }
    }

    // Puts the previous version back, if one was kept.
    public static string Rollback()
    {
        try
        {
            if (!Directory.Exists(BackupDir)) return "There is no earlier version kept.";
            var last = new DirectoryInfo(BackupDir).GetDirectories().OrderByDescending(d => d.LastWriteTime).FirstOrDefault(); if (last == null) return "There is no earlier version kept.";
            string dst = AppContext.BaseDirectory.TrimEnd('\\', '/'), exe = Environment.ProcessPath!, work = Path.Combine(Path.GetTempPath(), "perch-rollback.ps1");
            File.WriteAllText(work, string.Join("\r\n", "param($procId,$src,$dst,$exe)", "Wait-Process -Id $procId -ErrorAction SilentlyContinue", "Start-Sleep -Seconds 1", "robocopy $src $dst /E /NFL /NDL /NJH /NJS | Out-Null", "Start-Process -FilePath $exe"));
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{work}\" {Environment.ProcessId} \"{last.FullName}\" \"{dst}\" \"{exe}\"") { UseShellExecute = false, CreateNoWindow = true });
            System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
            return "";
        }
        catch (Exception e) { return "Could not roll back: " + e.Message; }
    }

    // Once a day, when switched on.
    public static void Daily()
    {
        var s = Settings.Current; if (!s.UpdateCheck) return;
        if (DateTime.TryParse(s.UpdateLastCheck, out var last) && (DateTime.Now - last).TotalHours < 24) return;
        _ = Task.Run(async () =>
        {
            var msg = await Check(false);
            if (msg.Length > 0 && Available != null) Svc.Ui(() => Svc.Notices.Add("Perch " + Available.Version + " is ready", "Open Settings, then Updates, to install it.", ""));
        });
    }
}
