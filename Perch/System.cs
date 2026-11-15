using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static Perch.UI;

namespace Perch;

// ───────────────────────── System tab ─────────────────────────
// Ports in use, a speed test, app updates, temporary files and drives, each a click away.
public class SystemTab : TabBase
{
    public override string Id => "system"; public override string Name => "System"; public override string Glyph => "";
    public override double Height => 284;
    readonly StackPanel _body = new(); string _page = "Ports"; StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    static readonly string[] Pages = { "Ports", "Speed", "Updates", "Clean", "Drives" };
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(40) };
    string _speed = "Press Run to measure your connection."; bool _busy;
    List<(string Name, string Id, string Ver, string New)>? _updates; string _updatesNote = "Looking for updates…";

    protected override FrameworkElement Build()
    {
        var scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        scroll.PreviewMouseWheel += (_, e) => { scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta * 0.6); e.Handled = true; };
        var g = MakeGrid("*", "auto,*"); g.Children.Add(_tabs.At(0, 0)); g.Children.Add(scroll.M(0, 10, 0, 0).At(1, 0));
        g.Margin = new Thickness(14, 4, 14, 12);
        Chips(); Fill();
        return g;
    }
    public override void Shown() { Fill(); }

    void Chips()
    {
        _tabs.Children.Clear();
        foreach (var p in Pages)
        {
            var name = p; bool on = name == _page;
            var c = Btn(T(name, 11, on ? "#000000" : "#E5E5EA", true), () => { _page = name; Chips(); Fill(); }, on ? "#FFFFFF" : Card, 9, new Thickness(10, 5, 10, 5));
            c.Margin = new Thickness(0, 0, 5, 0); _tabs.Children.Add(c);
        }
    }
    static TextBlock Note(string s) => T(s, 11, Dim).With(x => { x.TextWrapping = TextWrapping.Wrap; x.Margin = new Thickness(2, 0, 0, 8); });
    Border Line(string title, string sub, string? btn, Action? act, bool primary = false)
    {
        var left = Col(T(title, 12, "#FFFFFF", true).With(x => x.TextTrimming = TextTrimming.CharacterEllipsis), T(sub, 10.5, Dim).With(x => x.TextTrimming = TextTrimming.CharacterEllipsis));
        var row = MakeGrid("*,auto", "auto"); row.Children.Add(left.At(0, 0));
        if (btn != null && act != null) row.Children.Add(Pill(btn, act, primary).M(8).At(0, 1));
        var b = Box(row, Card, 12, new Thickness(11, 7, 8, 7)); b.Margin = new Thickness(0, 0, 0, 6); return b;
    }

    void Fill()
    {
        _body.Children.Clear();
        switch (_page)
        {
            case "Ports": Ports(); break;
            case "Speed": SpeedPage(); break;
            case "Updates": UpdatesPage(); break;
            case "Clean": CleanPage(); break;
            case "Drives": DrivesPage(); break;
        }
    }

    // ── Ports ──
    async void Ports()
    {
        _body.Children.Add(Note("Programs listening for connections on this PC."));
        var rows = await Task.Run(() =>
        {
            var list = new List<(int Port, int Pid, string Name)>();
            try
            {
                var psi = new ProcessStartInfo("netstat", "-ano -p TCP") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using var p = Process.Start(psi)!; string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(6000);
                foreach (var l in o.Split('\n'))
                {
                    var m = Regex.Match(l, @"TCP\s+\S+:(\d+)\s+\S+\s+LISTENING\s+(\d+)");
                    if (!m.Success) continue;
                    int port = int.Parse(m.Groups[1].Value), pid = int.Parse(m.Groups[2].Value);
                    if (list.Any(x => x.Port == port)) continue;
                    string name = "System"; try { name = Process.GetProcessById(pid).ProcessName; } catch { }
                    list.Add((port, pid, name));
                }
            }
            catch { }
            return list.OrderBy(x => x.Port).ToList();
        });
        if (_page != "Ports") return;
        if (rows.Count == 0) { _body.Children.Add(Note("Nothing is listening, or Windows would not say.")); return; }
        foreach (var r in rows.Take(60))
        {
            var row = r;
            _body.Children.Add(Line($"Port {row.Port}", row.Name + (row.Pid > 4 ? $"  ·  process {row.Pid}" : ""), row.Pid > 4 ? "Stop" : null, row.Pid > 4 ? () =>
            {
                IslandWindow.Instance.Hold(true);
                var ok = MessageBox.Show($"Stop {row.Name} (process {row.Pid}), which is using port {row.Port}?\n\nIt closes immediately and anything unsaved in it is lost.", "Perch", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
                IslandWindow.Instance.Hold(false);
                if (!ok) return;
                try { Process.GetProcessById(row.Pid).Kill(); Svc.Notices.Add("Stopped " + row.Name, $"Port {row.Port} is free again.", ""); } catch (Exception e) { Svc.Notices.Add("Could not stop it", e.Message, ""); }
                Fill();
            } : null));
        }
    }

    // ── Speed ──
    void SpeedPage()
    {
        _body.Children.Add(Note("Downloads a test file from Cloudflare and times it. Nothing about you is sent."));
        _body.Children.Add(Box(T(_speed, 13, "#FFFFFF", true).With(x => x.TextWrapping = TextWrapping.Wrap), Card, 12, new Thickness(12, 10, 12, 10)).With(b => b.Margin = new Thickness(0, 0, 0, 8)));
        _body.Children.Add(Pill(_busy ? "Measuring…" : "Run speed test", () => { if (!_busy) RunSpeed(); }, true).With(p => p.HorizontalAlignment = HorizontalAlignment.Left));
    }
    async void RunSpeed()
    {
        _busy = true; _speed = "Measuring…"; Fill();
        try
        {
            var lat = new List<double>();
            for (int i = 0; i < 3; i++) { var sw = Stopwatch.StartNew(); using var r = await Http.GetAsync("https://speed.cloudflare.com/__down?bytes=0"); sw.Stop(); lat.Add(sw.Elapsed.TotalMilliseconds); }
            var t = Stopwatch.StartNew(); long total = 0;
            using (var res = await Http.GetAsync("https://speed.cloudflare.com/__down?bytes=25000000", HttpCompletionOption.ResponseHeadersRead))
            using (var st = await res.Content.ReadAsStreamAsync())
            { var buf = new byte[81920]; int n; while ((n = await st.ReadAsync(buf)) > 0) total += n; }
            t.Stop();
            double mbps = total * 8 / 1e6 / Math.Max(0.05, t.Elapsed.TotalSeconds);
            _speed = $"{mbps:0.0} Mbps down   ·   {lat.Min():0} ms delay";
        }
        catch (Exception e) { _speed = "The test could not run: " + e.Message; }
        _busy = false; if (_page == "Speed") Fill();
    }

    // ── Updates (winget) ──
    async void UpdatesPage()
    {
        _body.Children.Add(Note("Apps with a newer version available, from Windows' own package manager."));
        if (_updates == null)
        {
            _body.Children.Add(Note(_updatesNote));
            var found = await Task.Run(ReadUpdates);
            _updates = found.List; _updatesNote = found.Note;
            if (_page != "Updates") return;
            Fill(); return;
        }
        if (_updates.Count == 0) { _body.Children.Add(Note(_updatesNote.Length > 0 ? _updatesNote : "Everything is up to date.")); }
        else
        {
            _body.Children.Add(Pill("Update all", () => Winget("upgrade --all --include-unknown --accept-package-agreements --accept-source-agreements"), true).With(p => { p.HorizontalAlignment = HorizontalAlignment.Left; p.Margin = new Thickness(0, 0, 0, 8); }));
            foreach (var u in _updates.Take(40))
            {
                var up = u;
                _body.Children.Add(Line(up.Name, $"{up.Ver} → {up.New}", "Update", () => Winget($"upgrade --id {up.Id} --accept-package-agreements --accept-source-agreements")));
            }
        }
        _body.Children.Add(Pill("Check again", () => { _updates = null; Fill(); }).With(p => p.HorizontalAlignment = HorizontalAlignment.Left));
    }
    static (List<(string, string, string, string)> List, string Note) ReadUpdates()
    {
        var list = new List<(string, string, string, string)>();
        try
        {
            var psi = new ProcessStartInfo("winget", "upgrade --include-unknown --accept-source-agreements") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
            using var p = Process.Start(psi)!; string o = p.StandardOutput.ReadToEnd(); if (!p.WaitForExit(45000)) { try { p.Kill(true); } catch { } return (list, "Windows' package manager took too long."); }
            var lines = o.Replace("\r", "").Split('\n').Select(l => l.TrimEnd()).ToList();
            int h = lines.FindIndex(l => Regex.IsMatch(l, @"^Name\s+Id\s+Version\s+Available"));
            if (h < 0) return (list, "Everything is up to date.");
            int cId = lines[h].IndexOf("Id", StringComparison.Ordinal), cVer = lines[h].IndexOf("Version", StringComparison.Ordinal), cNew = lines[h].IndexOf("Available", StringComparison.Ordinal);
            int cSrc = lines[h].IndexOf("Source", StringComparison.Ordinal); if (cSrc < 0) cSrc = int.MaxValue;
            foreach (var l in lines.Skip(h + 2))
            {
                if (l.Length <= cNew || l.StartsWith(" ") || Regex.IsMatch(l, @"^\d+ upgrades?")) continue;
                string F(int a, int b) => (l.Length > a ? l.Substring(a, Math.Min(b, l.Length) - a) : "").Trim();
                var row = (F(0, cId), F(cId, cVer), F(cVer, cNew), F(cNew, cSrc));
                if (row.Item2.Length > 0) list.Add(row);
            }
            return (list, list.Count == 0 ? "Everything is up to date." : "");
        }
        catch (Exception e) { return (list, "Windows' package manager (winget) is not available: " + e.Message); }
    }
    void Winget(string args)
    {
        // a visible window, so you can watch it and answer anything it asks
        try { Process.Start(new ProcessStartInfo("cmd.exe", $"/k winget {args}") { UseShellExecute = true }); } catch (Exception e) { Svc.Notices.Add("Could not start winget", e.Message, ""); }
        _updates = null;
    }

    // ── Clean ──
    void CleanPage()
    {
        string temp = Path.GetTempPath();
        _body.Children.Add(Note("Temporary files older than two days in your temp folder. Programs in use keep their files; only what Windows lets go is removed."));
        var info = T("Measuring…", 13, "#FFFFFF", true); var box = Box(info, Card, 12, new Thickness(12, 10, 12, 10)); box.Margin = new Thickness(0, 0, 0, 8); _body.Children.Add(box);
        var go = Pill("Clean up", () => { }, true); go.HorizontalAlignment = HorizontalAlignment.Left; go.Visibility = Visibility.Collapsed; _body.Children.Add(go);
        Task.Run(() => Scan(temp)).ContinueWith(t =>
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                var (files, bytes) = t.Result;
                info.Text = files == 0 ? "Nothing to clean." : $"{bytes / 1048576.0:0.0} MB in {files} old files";
                if (files == 0) return;
                go.Visibility = Visibility.Visible;
                go.MouseLeftButtonUp += async (_, _) =>
                {
                    IslandWindow.Instance.Hold(true);
                    var ok = MessageBox.Show($"Delete {files} old temporary files ({bytes / 1048576.0:0.0} MB) from {temp}?\n\nThey cannot be brought back.", "Perch", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
                    IslandWindow.Instance.Hold(false);
                    if (!ok) return;
                    int gone = await Task.Run(() => Delete(temp));
                    Svc.Notices.Add("Temp files cleaned", $"{gone} files removed.", ""); if (_page == "Clean") Fill();
                };
            });
        });
    }
    static IEnumerable<string> Old(string dir)
    {
        var cutoff = DateTime.Now.AddDays(-2);
        IEnumerable<string> files = Array.Empty<string>();
        try { files = Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }); } catch { }
        foreach (var f in files) { bool old = false; try { old = File.GetLastWriteTime(f) < cutoff && File.GetLastAccessTime(f) < cutoff; } catch { } if (old) yield return f; }
    }
    static (int, long) Scan(string dir) { int n = 0; long b = 0; foreach (var f in Old(dir)) { try { b += new FileInfo(f).Length; n++; } catch { } if (n > 50000) break; } return (n, b); }
    static int Delete(string dir) { int n = 0; foreach (var f in Old(dir).ToList()) { try { File.Delete(f); n++; } catch { } } return n; }

    // ── Drives ──
    void DrivesPage()
    {
        _body.Children.Add(Note("Eject removable drives safely."));
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                var drive = d; bool removable = d.DriveType == DriveType.Removable;
                string label = string.IsNullOrEmpty(d.VolumeLabel) ? d.Name.TrimEnd('\\') : $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})";
                _body.Children.Add(Line(label, $"{d.AvailableFreeSpace / 1073741824.0:0.#} GB free of {d.TotalSize / 1073741824.0:0.#} GB  ·  {d.DriveType}",
                    removable ? "Eject" : null, removable ? async () =>
                    {
                        string letter = drive.Name.TrimEnd('\\');
                        var r = await Extensions.Run($"(New-Object -ComObject Shell.Application).Namespace(17).ParseName('{letter}').InvokeVerb('Eject'); 'ok'", 15);
                        Svc.Notices.Add("Eject " + letter, r == "ok" ? "Windows has been asked to eject it." : r, "");
                    } : null));
            }
            catch { }
        }
    }
}
