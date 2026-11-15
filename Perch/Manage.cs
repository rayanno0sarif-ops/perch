using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Perch.UI;

namespace Perch;

// ───────────────────────── Manage tab ─────────────────────────
// Remove programs, see what is plugged in or paired, search and install from winget, clean caches, and prepare pictures.
public partial class ManageTab : TabBase
{
    public override string Id => "manage"; public override string Name => "Manage"; public override string Glyph => "";
    public override double Height => 300;
    readonly StackPanel _body = new(); readonly WrapPanel _tabs = new();
    static readonly string[] Pages = { "Uninstall", "Devices", "Install", "Cleaner", "Pictures", "Processes", "Wallpaper", "Disks", "Downloads" };
    string _page = "Uninstall"; string _query = ""; string _pkgMode = "Search";
    // each page asks for its own screen of rows; a newer click makes an older answer stale
    int _gen;

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
            var c = Btn(T(name, 11, on ? "#000000" : "#E5E5EA", true), () => { _page = name; _query = ""; Chips(); Fill(); }, on ? "#FFFFFF" : Card, 9, new Thickness(10, 5, 10, 5));
            c.Margin = new Thickness(0, 0, 5, 5); _tabs.Children.Add(c);
        }
    }
    static TextBlock Note(string s) => T(s, 11, Dim).With(x => { x.TextWrapping = TextWrapping.Wrap; x.Margin = new Thickness(2, 0, 0, 8); });
    Border Line(string title, string sub, string? btn, Action? act, bool primary = false, string? glyph = null)
    {
        var left = Col(T(title, 12, "#FFFFFF", true).With(x => x.TextTrimming = TextTrimming.CharacterEllipsis), T(sub, 10.5, Dim).With(x => x.TextTrimming = TextTrimming.CharacterEllipsis));
        var row = MakeGrid(glyph != null ? "auto,*,auto" : "*,auto", "auto");
        int c = 0;
        if (glyph != null) row.Children.Add(Ico(glyph, 14, "#D1D1D6").M(0, 0, 10).With(x => x.VerticalAlignment = VerticalAlignment.Center).At(0, c++));
        row.Children.Add(left.At(0, c++));
        if (btn != null && act != null) row.Children.Add(Pill(btn, act, primary).M(8).At(0, c));
        var b = Box(row, Card, 12, new Thickness(11, 7, 8, 7)); b.Margin = new Thickness(0, 0, 0, 6); return b;
    }
    TextBox SearchBox(string hint, Action<string> go)
    {
        var tb = new TextBox { Text = _query, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), CaretBrush = Brushes.White, FontFamily = Font, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, ToolTip = hint, MinWidth = 220 };
        tb.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { _query = tb.Text.Trim(); go(_query); } };
        var holder = new Grid(); var h = T(hint, 12, Dim); h.IsHitTestVisible = false; holder.Children.Add(h); holder.Children.Add(tb);
        tb.TextChanged += (_, _) => h.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        h.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var box = Box(Row(Ico("", 12, Dim).M(0, 0, 8), holder), Card, 10, new Thickness(11, 6, 11, 6)); box.Margin = new Thickness(0, 0, 0, 8);
        _body.Children.Add(box);
        return tb;
    }

    void Fill()
    {
        _body.Children.Clear(); _gen++;
        switch (_page)
        {
            case "Uninstall": UninstallPage(); break;
            case "Devices": DevicesPage(); break;
            case "Install": InstallPage(); break;
            case "Cleaner": CleanerPage(); break;
            case "Pictures": PicturesPage(); break;
            case "Processes": ProcessesPage(); break;
            case "Wallpaper": WallpaperPage(); break;
            case "Disks": DisksPage(); break;
            case "Downloads": DownloadsPage(); break;
        }
    }

    // ───────── Uninstall ─────────
    static List<Program_>? _programs;
    public class Program_ { public string Name = "", Publisher = "", Version = "", Uninstall = "", Quiet = "", Location = "", Icon = ""; public long Size; }
    static List<Program_> ReadPrograms()
    {
        var list = new List<Program_>();
        void Scan(Microsoft.Win32.RegistryKey root, string path)
        {
            try
            {
                using var k = root.OpenSubKey(path); if (k == null) return;
                foreach (var name in k.GetSubKeyNames())
                {
                    try
                    {
                        using var a = k.OpenSubKey(name); if (a == null) continue;
                        string dn = a.GetValue("DisplayName") as string ?? ""; if (dn.Length == 0) continue;
                        if (a.GetValue("SystemComponent") is int sc && sc == 1) continue;
                        if (a.GetValue("ParentKeyName") != null || a.GetValue("ReleaseType") is string rt && (rt.Contains("Update") || rt.Contains("Hotfix"))) continue;
                        string un = a.GetValue("UninstallString") as string ?? ""; string qu = a.GetValue("QuietUninstallString") as string ?? "";
                        if (un.Length == 0 && qu.Length == 0) continue;
                        if (list.Any(x => x.Name == dn && x.Version == (a.GetValue("DisplayVersion") as string ?? ""))) continue;
                        list.Add(new Program_
                        {
                            Name = dn, Publisher = a.GetValue("Publisher") as string ?? "", Version = a.GetValue("DisplayVersion") as string ?? "", Uninstall = un, Quiet = qu,
                            Location = (a.GetValue("InstallLocation") as string ?? "").Trim('"'), Icon = a.GetValue("DisplayIcon") as string ?? "",
                            Size = a.GetValue("EstimatedSize") is int kb ? kb * 1024L : 0
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }
        Scan(Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        Scan(Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
        Scan(Microsoft.Win32.Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    async void UninstallPage()
    {
        int gen = _gen;
        _body.Children.Add(Note("Programs installed on this PC. Uninstall runs the program's own uninstaller, then offers to clear what it left behind, always to the Recycle Bin."));
        var results = new StackPanel();
        SearchBox("Search programs", q => ShowPrograms(results, q));
        _body.Children.Add(results);
        results.Children.Add(Note("Reading the list…"));
        _programs ??= await Task.Run(ReadPrograms);
        if (gen != _gen) return;
        ShowPrograms(results, _query);
    }
    void ShowPrograms(StackPanel host, string q)
    {
        host.Children.Clear();
        var rows = (_programs ?? new()).Where(p => q.Length == 0 || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(40).ToList();
        if (rows.Count == 0) { host.Children.Add(Note("No program matches.")); return; }
        foreach (var p in rows)
        {
            var prog = p;
            string sub = string.Join("  ·  ", new[] { prog.Publisher, prog.Version, prog.Size > 0 ? $"{prog.Size / 1048576.0:0} MB" : "" }.Where(x => x.Length > 0));
            host.Children.Add(Line(prog.Name, sub, "Uninstall", () => Uninstall(prog)));
        }
    }
    async void Uninstall(Program_ p)
    {
        IslandWindow.Instance.Hold(true);
        var ok = MessageBox.Show($"Uninstall {p.Name}?\n\nIts own uninstaller opens and may ask you more questions. Anything unsaved in it is lost.", "Perch", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        IslandWindow.Instance.Hold(false);
        if (!ok) return;
        string cmd = p.Uninstall.Length > 0 ? p.Uninstall : p.Quiet;
        if (Regex.IsMatch(cmd, @"msiexec(\.exe)?\s+/I", RegexOptions.IgnoreCase)) cmd = Regex.Replace(cmd, @"/I", "/X", RegexOptions.IgnoreCase); // an "install" entry means remove here
        Svc.Notices.Add("Uninstalling " + p.Name, "Follow the uninstaller's window.", "");
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c \"" + cmd + "\"") { UseShellExecute = false, CreateNoWindow = true };
            using var proc = Process.Start(psi)!;
            await Task.Run(() => proc.WaitForExit(30 * 60 * 1000));
        }
        catch (Exception e) { Svc.Notices.Add("Could not start the uninstaller", e.Message, ""); return; }
        _programs = null;
        var left = await Task.Run(() => Leftovers(p));
        if (_page == "Uninstall") Fill();
        if (left.Count == 0) { Svc.Notices.Add(p.Name + " is gone", "No leftover folders were found.", ""); return; }
        long total = left.Sum(l => l.Size);
        IslandWindow.Instance.Hold(true);
        var clear = MessageBox.Show($"{p.Name} left {left.Count} folder{(left.Count == 1 ? "" : "s")} behind ({total / 1048576.0:0.0} MB):\n\n" + string.Join("\n", left.Select(l => l.Path)) + "\n\nMove them to the Recycle Bin?", "Perch", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        IslandWindow.Instance.Hold(false);
        if (!clear) return;
        int n = 0;
        foreach (var l in left) { try { Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(l.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); n++; } catch { } }
        Svc.Notices.Add("Leftovers moved to the Recycle Bin", $"{n} folder{(n == 1 ? "" : "s")}.", "");
    }
    // folders that carry the program's own name, in the places programs keep their data
    static List<(string Path, long Size)> Leftovers(Program_ p)
    {
        var found = new List<(string, long)>(); var names = new List<string> { p.Name };
        string plain = Regex.Replace(p.Name, @"\s+v?\d[\d.]*.*$", "").Trim(); if (plain.Length >= 3 && !names.Contains(plain)) names.Add(plain);
        var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) };
        foreach (var r in roots)
            foreach (var n in names)
            {
                string path = System.IO.Path.Combine(r, n);
                if (Directory.Exists(path) && !found.Any(f => f.Item1 == path)) found.Add((path, DirSize(path)));
                if (p.Publisher.Length > 2) { string pp = System.IO.Path.Combine(r, p.Publisher, n); if (Directory.Exists(pp) && !found.Any(f => f.Item1 == pp)) found.Add((pp, DirSize(pp))); }
            }
        if (p.Location.Length > 3 && Directory.Exists(p.Location) && !found.Any(f => f.Item1 == p.Location)) found.Add((p.Location, DirSize(p.Location)));
        return found;
    }
    static long DirSize(string dir)
    {
        long n = 0;
        try { foreach (var f in Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })) { try { n += new FileInfo(f).Length; } catch { } } } catch { }
        return n;
    }

    // ───────── Devices ─────────
    static string Ps(string script, int seconds = 25)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
            using var p = Process.Start(psi)!;
            var read = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(seconds * 1000)) { try { p.Kill(true); } catch { } return ""; }
            return read.Result;
        }
        catch { return ""; }
    }
    public static List<(string Kind, string Name, string State, int Battery)> ReadDevices()
    {
        const string script = @"
$ErrorActionPreference='SilentlyContinue'
$out=@()
Get-PnpDevice -PresentOnly | Where-Object { $_.Class -in 'Bluetooth','Camera','Image','Monitor','Printer','Mouse','Keyboard' -and $_.FriendlyName } | ForEach-Object {
  $n=$_.FriendlyName
  if ($_.Class -eq 'Bluetooth' -and ($n -match 'Enumerator|Radio|Adapter|Generic|Transport|Protocol|Service|Wireless Bluetooth$|Intel|Realtek|MediaTek|Qualcomm')) { return }
  if ($_.Class -in 'Mouse','Keyboard' -and ($n -match 'HID|PS/2|Standard|Virtual|Terminal|Remote')) { return }
  $conn=(Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName '{83DA6326-97A6-4088-9453-A1923F573B29} 15').Data
  $b=(Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName '{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2').Data
  $st= if ($conn -eq $true) {'connected'} elseif ($conn -eq $false) {'paired'} else {'present'}
  if ($b -eq $null) { $b=-1 }
  $out += ('{0}|{1}|{2}|{3}' -f $_.Class,$n,$st,$b)
}
$out | Sort-Object -Unique
";
        var list = new List<(string, string, string, int)>();
        foreach (var l in Ps(script).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = l.Split('|'); if (f.Length < 4) continue;
            list.Add((f[0], f[1].Trim(), f[2], int.TryParse(f[3], out var b) ? b : -1));
        }
        return list;
    }
    async void DevicesPage()
    {
        int gen = _gen;
        _body.Children.Add(Note("Bluetooth devices, cameras, displays, printers and the mouse or keyboard Windows knows by name. Battery shows for Bluetooth devices that report it."));
        _body.Children.Add(Note("Looking…"));
        var devs = await Task.Run(ReadDevices);
        if (gen != _gen) return;
        _body.Children.RemoveAt(_body.Children.Count - 1);
        if (devs.Count == 0) { _body.Children.Add(Note("Nothing was found, or Windows would not say.")); return; }
        foreach (var d in devs.OrderBy(d => d.State == "connected" ? 0 : 1).ThenBy(d => d.Kind).ThenBy(d => d.Name))
        {
            string glyph = d.Kind switch { "Bluetooth" => "", "Camera" or "Image" => "", "Monitor" => "", "Printer" => "", "Mouse" => "", _ => "" };
            string sub = d.Kind + "  ·  " + d.State + (d.Battery >= 0 ? $"  ·  {d.Battery}% battery" : "");
            _body.Children.Add(Line(d.Name, sub, null, null, false, glyph));
        }
    }

    // ───────── Install (winget) ─────────
    static List<(string Name, string Id, string Ver)> ParseTable(string o)
    {
        var rows = new List<(string, string, string)>();
        var lines = o.Replace("\r", "").Split('\n').Select(l => l.TrimEnd()).ToList();
        int h = lines.FindIndex(l => Regex.IsMatch(l, @"^Name\s+Id\s+Version"));
        if (h < 0) return rows;
        int cId = lines[h].IndexOf("Id", StringComparison.Ordinal), cVer = lines[h].IndexOf("Version", StringComparison.Ordinal);
        int cEnd = new[] { lines[h].IndexOf("Available", StringComparison.Ordinal), lines[h].IndexOf("Match", StringComparison.Ordinal), lines[h].IndexOf("Source", StringComparison.Ordinal) }.Where(x => x > cVer).DefaultIfEmpty(int.MaxValue).Min();
        foreach (var l in lines.Skip(h + 2))
        {
            if (l.Length <= cVer || l.StartsWith(" ")) continue;
            string F(int a, int b) => (l.Length > a ? l.Substring(a, Math.Min(b, l.Length) - a) : "").Trim();
            var row = (F(0, cId), F(cId, cVer), F(cVer, cEnd));
            if (row.Item2.Length > 0) rows.Add(row);
        }
        return rows;
    }
    void InstallPage()
    {
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var m in new[] { "Search", "Installed" })
        {
            var mode = m; bool on = mode == _pkgMode;
            modes.Children.Add(Btn(T(mode, 11, on ? "#000000" : "#E5E5EA", true), () => { _pkgMode = mode; _query = ""; Fill(); }, on ? "#FFFFFF" : Card, 9, new Thickness(10, 5, 10, 5)).With(c => c.Margin = new Thickness(0, 0, 5, 0)));
        }
        _body.Children.Add(modes);
        var results = new StackPanel();
        if (_pkgMode == "Search")
        {
            _body.Children.Add(Note("Finds apps in Windows' package manager (winget) and installs or removes them without opening a store. Press Enter to search."));
            SearchBox("Search for an app", q => PackageSearch(results, q));
            _body.Children.Add(results);
            if (_query.Length > 0) PackageSearch(results, _query);
        }
        else
        {
            _body.Children.Add(Note("Apps winget manages on this PC. Remove opens a window so you can watch it and answer anything it asks."));
            SearchBox("Filter the list", q => ShowInstalled(results, q));
            _body.Children.Add(results);
            LoadInstalled(results);
        }
    }
    static List<(string Name, string Id, string Ver)>? _installed;
    async void PackageSearch(StackPanel host, string q)
    {
        int gen = _gen; host.Children.Clear();
        if (q.Length == 0) return;
        host.Children.Add(Note("Searching…"));
        var rows = await Task.Run(() => ParseTable(Ps($"winget search \"{q.Replace("\"", "")}\" --accept-source-agreements --disable-interactivity", 40)));
        if (gen != _gen) return;
        host.Children.Clear();
        if (rows.Count == 0) { host.Children.Add(Note("Nothing found, or winget is not available.")); return; }
        foreach (var r in rows.Take(25))
        {
            var row = r;
            host.Children.Add(Line(row.Name, row.Id + (row.Ver.Length > 0 ? "  ·  " + row.Ver : ""), "Install", () => Winget($"install --id {row.Id} -e --accept-package-agreements --accept-source-agreements")));
        }
    }
    async void LoadInstalled(StackPanel host)
    {
        int gen = _gen;
        host.Children.Add(Note("Reading the list…"));
        _installed ??= await Task.Run(() => ParseTable(Ps("winget list --accept-source-agreements --disable-interactivity", 40)));
        if (gen != _gen) return;
        ShowInstalled(host, _query);
    }
    void ShowInstalled(StackPanel host, string q)
    {
        host.Children.Clear();
        var rows = (_installed ?? new()).Where(r => q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Id.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(40).ToList();
        if (rows.Count == 0) { host.Children.Add(Note("Nothing to show.")); return; }
        foreach (var r in rows)
        {
            var row = r;
            host.Children.Add(Line(row.Name, row.Id + (row.Ver.Length > 0 ? "  ·  " + row.Ver : ""), "Remove", () =>
            {
                IslandWindow.Instance.Hold(true);
                bool ok = MessageBox.Show($"Remove {row.Name}?", "Perch", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
                IslandWindow.Instance.Hold(false);
                if (ok) { Winget($"uninstall --id {row.Id} -e"); _installed = null; }
            }));
        }
    }
    static void Winget(string args)
    {
        try { Process.Start(new ProcessStartInfo("cmd.exe", $"/k winget {args}") { UseShellExecute = true }); } catch (Exception e) { Svc.Notices.Add("Could not start winget", e.Message, ""); }
    }

    // ───────── Cleaner ─────────
    // Each category is a few folders that programs rebuild by themselves. Files in use are skipped.
    public record CleanItem(string Id, string Title, string Why, Func<IEnumerable<string>> Folders, bool OnlyOld);
    static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    internal static readonly CleanItem[] CleanItems =
    {
        new("temp", "Temporary files", "Files older than two days in your temp folder.", () => new[] { Path.GetTempPath() }, true),
        new("thumbs", "Thumbnail cache", "Windows rebuilds the little previews in Explorer.", () => new[] { Path.Combine(Local, @"Microsoft\Windows\Explorer") }, false),
        new("browser", "Browser caches", "Cached pages and images in Edge, Chrome, Brave and Firefox. You stay signed in.", () => BrowserCaches(), false),
        new("crash", "Crash reports and dumps", "Error reports Windows and programs keep after a crash.", () => new[] { Path.Combine(Local, "CrashDumps"), Path.Combine(Local, @"Microsoft\Windows\WER") }, false),
        new("gpu", "Shader caches", "Graphics caches games and drivers rebuild on their next run.", () => new[] { Path.Combine(Local, @"D3DSCache"), Path.Combine(Local, @"NVIDIA\DXCache"), Path.Combine(Local, @"NVIDIA\GLCache"), Path.Combine(Local, @"AMD\DxCache") }, false),
    };
    static IEnumerable<string> BrowserCaches()
    {
        var l = new List<string>();
        foreach (var b in new[] { @"Microsoft\Edge", @"Google\Chrome", @"BraveSoftware\Brave-Browser" })
        {
            string ud = Path.Combine(Local, b, "User Data");
            if (!Directory.Exists(ud)) continue;
            try
            {
                foreach (var prof in Directory.GetDirectories(ud).Where(d => Path.GetFileName(d) is "Default" || Path.GetFileName(d).StartsWith("Profile ")))
                    foreach (var c in new[] { @"Cache\Cache_Data", "Code Cache", "GPUCache" }) l.Add(Path.Combine(prof, c));
            }
            catch { }
        }
        string ff = Path.Combine(Local, @"Mozilla\Firefox\Profiles");
        try { if (Directory.Exists(ff)) foreach (var p in Directory.GetDirectories(ff)) l.Add(Path.Combine(p, "cache2")); } catch { }
        return l;
    }
    internal static IEnumerable<string> Files(CleanItem it)
    {
        var cutoff = DateTime.Now.AddDays(-2);
        foreach (var dir in it.Folders())
        {
            IEnumerable<string> files = Array.Empty<string>();
            try
            {
                if (!Directory.Exists(dir)) continue;
                files = Directory.EnumerateFiles(dir, it.Id == "thumbs" ? "thumbcache_*.db" : "*", new EnumerationOptions { RecurseSubdirectories = it.Id != "thumbs", IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint });
            }
            catch { }
            foreach (var f in files)
            {
                bool take = true;
                if (it.OnlyOld) { try { take = File.GetLastWriteTime(f) < cutoff && File.GetLastAccessTime(f) < cutoff; } catch { take = false; } }
                if (take) yield return f;
            }
        }
    }
    readonly Dictionary<string, (int N, long Bytes)> _sizes = new(); readonly HashSet<string> _picked = new();
    void CleanerPage()
    {
        _body.Children.Add(Note("Pick what to clear. Nothing is deleted until you press Clean, and you are asked once more with the total."));
        var list = new StackPanel(); _body.Children.Add(list);
        var total = T("", 11, Dim); total.Margin = new Thickness(2, 4, 0, 6); _body.Children.Add(total);
        var go = Pill("Clean selected", () => { }, true); go.HorizontalAlignment = HorizontalAlignment.Left; _body.Children.Add(go);
        void Paint()
        {
            list.Children.Clear();
            foreach (var it in CleanItems)
            {
                var item = it; bool on = _picked.Contains(item.Id);
                string size = _sizes.TryGetValue(item.Id, out var s) ? (s.N == 0 ? "nothing to clean" : $"{s.Bytes / 1048576.0:0.0} MB in {s.N} files") : "measuring…";
                var tick = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5), BorderBrush = B(on ? "#FFFFFF" : "#636366"), BorderThickness = new Thickness(1.4), Background = on ? Brushes.White : Brushes.Transparent, Child = on ? Ico("", 10, "#000000") : null };
                var row = MakeGrid("auto,*", "auto");
                row.Children.Add(tick.M(0, 0, 11).With(x => x.VerticalAlignment = VerticalAlignment.Center).At(0, 0));
                row.Children.Add(Col(T(item.Title, 12, "#FFFFFF", true), T(item.Why + "  " + size, 10.5, Dim).With(x => x.TextWrapping = TextWrapping.Wrap)).At(0, 1));
                var b = Btn(row, () => { if (!_picked.Remove(item.Id)) _picked.Add(item.Id); Paint(); }, Card, 12, new Thickness(11, 8, 11, 8)); b.Margin = new Thickness(0, 0, 0, 6);
                list.Children.Add(b);
            }
            int n = _picked.Sum(id => _sizes.TryGetValue(id, out var z) ? z.N : 0); long bytes = _picked.Sum(id => _sizes.TryGetValue(id, out var z) ? z.Bytes : 0);
            total.Text = _picked.Count == 0 ? "Nothing selected." : $"{bytes / 1048576.0:0.0} MB in {n} files selected.";
            go.Opacity = _picked.Count == 0 || n == 0 ? 0.4 : 1;
        }
        go.MouseLeftButtonUp += async (_, _) =>
        {
            var chosen = CleanItems.Where(i => _picked.Contains(i.Id)).ToList(); if (chosen.Count == 0) return;
            int n = chosen.Sum(i => _sizes.TryGetValue(i.Id, out var z) ? z.N : 0); long bytes = chosen.Sum(i => _sizes.TryGetValue(i.Id, out var z) ? z.Bytes : 0); if (n == 0) return;
            IslandWindow.Instance.Hold(true);
            bool ok = MessageBox.Show($"Delete {n} files ({bytes / 1048576.0:0.0} MB) from:\n\n" + string.Join("\n", chosen.Select(i => "• " + i.Title)) + "\n\nThey cannot be brought back. Programs rebuild their caches by themselves.", "Perch", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            IslandWindow.Instance.Hold(false);
            if (!ok) return;
            int gone = await Task.Run(() => { int c = 0; foreach (var it in chosen) foreach (var f in Files(it).ToList()) { try { File.Delete(f); c++; } catch { } } return c; });
            Svc.Notices.Add("Cleaned up", $"{gone} files removed.", "");
            _sizes.Clear(); _picked.Clear(); Fill();
        };
        Paint();
        CleanerSchedule();
        int gen = _gen;
        foreach (var it in CleanItems)
        {
            var item = it;
            _ = Task.Run(() => { int n = 0; long b = 0; foreach (var f in Files(item)) { try { b += new FileInfo(f).Length; n++; } catch { } if (n > 60000) break; } return (n, b); })
                .ContinueWith(t => Application.Current?.Dispatcher.BeginInvoke(() => { if (gen != _gen) return; _sizes[item.Id] = t.Result; Paint(); }));
        }
    }

    // ───────── Pictures ─────────
    readonly List<string> _pics = new(); string _fmt = "png"; int _maxW; string _mark = "";
    void PicturesPage()
    {
        _body.Children.Add(Note("Convert, shrink and watermark pictures here, all on this PC. Originals are never touched: results go to a “Perch converted” folder beside them."));
        var pick = Pill("Choose pictures…", () =>
        {
            IslandWindow.Instance.Hold(true);
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic|All files|*.*" };
            if (dlg.ShowDialog() == true) { _pics.Clear(); _pics.AddRange(dlg.FileNames); }
            IslandWindow.Instance.Hold(false); Fill();
        });
        var shelf = Pill("From the shelf", () =>
        {
            var pics = Svc.Shelf.Where(f => Regex.IsMatch(f, @"\.(png|jpe?g|bmp|gif|tiff?)$", RegexOptions.IgnoreCase) && File.Exists(f)).ToList();
            _pics.Clear(); _pics.AddRange(pics); Fill();
        });
        _body.Children.Add(Row(pick.M(0, 0, 6), shelf).With(r => r.Margin = new Thickness(0, 0, 0, 8)));
        _body.Children.Add(Note(_pics.Count == 0 ? "No pictures chosen yet." : $"{_pics.Count} picture{(_pics.Count == 1 ? "" : "s")} chosen."));
        var fmt = new Segmented(new[] { "PNG", "JPEG" }, _fmt == "jpg" ? 1 : 0, i => _fmt = i == 1 ? "jpg" : "png");
        _body.Children.Add(Row(T("Format", 11.5, "#FFFFFF", true).M(0, 0, 12).With(x => x.VerticalAlignment = VerticalAlignment.Center), fmt).With(r => r.Margin = new Thickness(0, 0, 0, 8)));
        int[] widths = { 0, 3840, 1920, 1280, 800 };
        var size = new Segmented(new[] { "Original", "4K", "1080p", "720p", "800 px" }, Math.Max(0, Array.IndexOf(widths, _maxW)), i => _maxW = widths[i]);
        _body.Children.Add(Row(T("Width", 11.5, "#FFFFFF", true).M(0, 0, 12).With(x => x.VerticalAlignment = VerticalAlignment.Center), size).With(r => r.Margin = new Thickness(0, 0, 0, 8)));
        var wm = new TextBox { Text = _mark, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), CaretBrush = Brushes.White, FontFamily = Font, FontSize = 12, MinWidth = 200, ToolTip = "Watermark text (optional)" };
        wm.TextChanged += (_, _) => _mark = wm.Text;
        _body.Children.Add(Row(T("Watermark", 11.5, "#FFFFFF", true).M(0, 0, 12).With(x => x.VerticalAlignment = VerticalAlignment.Center), Box(wm, Card, 10, new Thickness(10, 5, 10, 5))).With(r => r.Margin = new Thickness(0, 0, 0, 10)));
        var status = T("", 11, Dim); status.TextWrapping = TextWrapping.Wrap;
        var go = Pill("Convert", async () =>
        {
            if (_pics.Count == 0) { status.Text = "Choose some pictures first."; return; }
            status.Text = "Working…";
            var (done, dir, failed) = await Task.Run(() => ConvertPictures(_pics.ToList(), _fmt, _maxW, _mark));
            status.Text = done == 0 ? "Nothing could be converted." : $"{done} converted" + (failed > 0 ? $", {failed} could not be read" : "") + ".";
            if (done > 0 && dir != null) Svc.Open(dir);
        }, true);
        _body.Children.Add(go.With(x => x.HorizontalAlignment = HorizontalAlignment.Left)); _body.Children.Add(status.M(2, 8, 0, 0));
    }
    static (int, string?, int) ConvertPictures(List<string> files, string fmt, int maxW, string mark)
    {
        int done = 0, failed = 0; string? last = null;
        foreach (var f in files)
        {
            try
            {
                var dec = BitmapDecoder.Create(new Uri(f), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapSource src = dec.Frames[0];
                if (maxW > 0 && src.PixelWidth > maxW) src = new TransformedBitmap(src, new ScaleTransform((double)maxW / src.PixelWidth, (double)maxW / src.PixelWidth));
                if (mark.Trim().Length > 0)
                {
                    int w = src.PixelWidth, h = src.PixelHeight;
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        dc.DrawImage(src, new Rect(0, 0, w, h));
                        double fs = Math.Max(14, h / 24.0);
                        var ft = new FormattedText(mark, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), fs, new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1.0);
                        var pos = new Point(w - ft.Width - fs, h - ft.Height - fs * 0.8);
                        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), null, new Rect(pos.X - fs * 0.5, pos.Y - fs * 0.25, ft.Width + fs, ft.Height + fs * 0.5), fs * 0.4, fs * 0.4);
                        dc.DrawText(ft, pos);
                    }
                    var rt = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); rt.Render(dv); src = rt;
                }
                if (fmt == "jpg" && src.Format.BitsPerPixel == 32) src = new FormatConvertedBitmap(src, PixelFormats.Bgr32, null, 0);
                BitmapEncoder enc = fmt == "jpg" ? new JpegBitmapEncoder { QualityLevel = 90 } : new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(src));
                string dir = Path.Combine(Path.GetDirectoryName(f)!, "Perch converted"); Directory.CreateDirectory(dir);
                string target = Path.Combine(dir, Path.GetFileNameWithoutExtension(f) + (fmt == "jpg" ? ".jpg" : ".png"));
                for (int i = 2; File.Exists(target); i++) target = Path.Combine(dir, Path.GetFileNameWithoutExtension(f) + $" ({i})" + (fmt == "jpg" ? ".jpg" : ".png")); // never overwrite
                using (var fs2 = File.Create(target)) enc.Save(fs2);
                done++; last = dir;
            }
            catch { failed++; }
        }
        return (done, last, failed);
    }
}

// ───────────────────────── Background housekeeping ─────────────────────────
// Quit-when-closed, keep-awake rules, sound output priority and Bluetooth during sleep. Called once a second.
public static class Housekeeping
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc fn, IntPtr p);
    delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int size);

    static int _n; static bool _busy;
    public static void Tick()
    {
        var s = Settings.Current; _n++;
        try
        {
            if (_n % 3 == 0 && s.AutoQuitApps.Count > 0 && !_busy) { _busy = true; Task.Run(AutoQuit).ContinueWith(_ => _busy = false); }
            if (_n % 5 == 0) KeepAwakeRules(s);
            if (_n % 3 == 1 && (s.AudioPriorityOn || s.HeadphoneDropVolume >= 0)) Audio(s);
            Scheduled.Tick(s);
        }
        catch { }
    }

    // ── quit when the last window closes ──
    static readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase); static readonly Dictionary<string, DateTime> _bare = new(), _asked = new();
    static void AutoQuit()
    {
        foreach (var exe in Settings.Current.AutoQuitApps.ToList())
        {
            string name = Path.GetFileNameWithoutExtension(exe);
            var procs = Process.GetProcessesByName(name);
            if (procs.Length == 0) { _seen.Remove(exe); _bare.Remove(exe); continue; }
            var pids = new HashSet<uint>(procs.Select(p => (uint)p.Id)); foreach (var p in procs) p.Dispose();
            bool windowed = false; var all = new List<IntPtr>();
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (!pids.Contains(pid)) return true;
                all.Add(h);
                if (IsWindowVisible(h) && GetWindowTextLength(h) > 0 && (GetWindowLong(h, -20) & 0x80) == 0 && !(DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0)) windowed = true;
                return true;
            }, IntPtr.Zero);
            if (windowed) { _seen.Add(exe); _bare.Remove(exe); continue; }
            if (!_seen.Contains(exe)) continue; // it has not shown a window yet, so it is still starting
            if (!_bare.TryGetValue(exe, out var since)) { _bare[exe] = DateTime.Now; continue; }
            if (DateTime.Now - since < TimeSpan.FromSeconds(4)) continue;
            if (_asked.TryGetValue(exe, out var at) && DateTime.Now - at < TimeSpan.FromMinutes(2)) continue;
            _asked[exe] = DateTime.Now;
            foreach (var h in all) PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE: a polite request, never a kill
            Svc.Ui(() => Svc.Notices.Add("Asked " + name + " to quit", "Its last window was closed.", ""));
        }
    }

    // ── keep awake while chosen programs run or the charger is in ──
    static bool _ruleAwake; static DateTime _jiggleAt = DateTime.MinValue;
    static void KeepAwakeRules(Settings s)
    {
        bool want = false;
        if (s.AwakeOnCharger) { try { want = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online; } catch { } }
        if (!want && s.AwakeWhileApps && s.AwakeApps.Count > 0)
            want = s.AwakeApps.Any(a => { try { var p = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(a)); bool any = p.Length > 0; foreach (var x in p) x.Dispose(); return any; } catch { return false; } });
        if (!want && s.AwakeExternal && System.Windows.Forms.Screen.AllScreens.Length > 1) want = true;
        if (s.AwakePauseLocked && Tools2.Locked) want = false;
        if (s.AwakeJiggle && (want || Tools.KeepAwake) && !Tools2.Locked && (DateTime.Now - _jiggleAt).TotalSeconds >= Math.Max(15, s.AwakeJiggleSec)) { _jiggleAt = DateTime.Now; Hooks.Jiggle(); }
        if (want == _ruleAwake) return;
        _ruleAwake = want;
        Svc.Ui(() => Tools.RuleAwake(want));
    }

    // ── sound output priority, and a gentler volume when headphones go away ──
    static HashSet<string>? _outs; static string _lastDefault = "";
    static void Audio(Settings s)
    {
        Svc.Ui(() =>
        {
            try
            {
                var outs = Mixer.Outputs(); var ids = new HashSet<string>(outs.Select(o => o.Id));
                string def = outs.FirstOrDefault(o => o.Default).Id ?? "";
                foreach (var o in outs) if (!s.AudioPriority.Contains(o.Id) && s.AudioPriorityOn) { s.AudioPriority.Add(o.Id); s.Save(); }
                if (_outs != null && !_outs.SetEquals(ids))
                {
                    bool gone = _outs.Contains(_lastDefault) && !ids.Contains(_lastDefault);
                    if (s.AudioPriorityOn)
                    {
                        var best = s.AudioPriority.FirstOrDefault(id => ids.Contains(id));
                        if (best != null && best != def && Mixer.SetOutput(best)) { def = best; Svc.Notices.Add("Sound output switched", outs.First(o => o.Id == best).Name, ""); }
                    }
                    if (gone && s.HeadphoneDropVolume >= 0 && Svc.Speaker.Volume > s.HeadphoneDropVolume / 100.0)
                    {
                        Svc.Speaker.Volume = s.HeadphoneDropVolume / 100.0;
                        Svc.Notices.Add("Volume lowered", $"The sound output went away, so the volume is now {s.HeadphoneDropVolume}%.", "");
                    }
                }
                _outs = ids; _lastDefault = def;
            }
            catch { }
        });
    }

    // ── Bluetooth off during sleep, back on at wake only if Perch turned it off ──
    static bool _btWasOn;
    public static void Start()
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
        {
            try
            {
                if (!Settings.Current.BluetoothSleep) return;
                if (e.Mode == Microsoft.Win32.PowerModes.Suspend) SetBluetooth(false, true);
                else if (e.Mode == Microsoft.Win32.PowerModes.Resume && _btWasOn) { _btWasOn = false; SetBluetooth(true, false); }
            }
            catch { }
        };
    }
    static void SetBluetooth(bool on, bool remember)
    {
        try
        {
            var radios = Windows.Devices.Radios.Radio.GetRadiosAsync().AsTask().Result;
            foreach (var r in radios.Where(r => r.Kind == Windows.Devices.Radios.RadioKind.Bluetooth))
            {
                if (!on && r.State == Windows.Devices.Radios.RadioState.On) { if (remember) _btWasOn = true; r.SetStateAsync(Windows.Devices.Radios.RadioState.Off).AsTask().Wait(1500); }
                else if (on && r.State == Windows.Devices.Radios.RadioState.Off) r.SetStateAsync(Windows.Devices.Radios.RadioState.On).AsTask().Wait(3000);
            }
        }
        catch { }
    }
}

// ───────────────────────── Radial menu ─────────────────────────
// A ring of round buttons around the pointer. Press the shortcut, click one, and it runs.
public static class Radial
{
    public static readonly (string Id, string Label, string Glyph)[] Catalog =
    {
        ("screenshot", "Screenshot", ""), ("playpause", "Play / pause", ""), ("next", "Next track", ""), ("prev", "Previous track", ""),
        ("mute", "Mute microphone", ""), ("colour", "Pick colour", ""), ("copytext", "Copy text", ""), ("lock", "Lock", ""),
        ("desktop", "Show desktop", ""), ("taskview", "Task view", ""), ("awake", "Keep awake", ""), ("clean", "Cleaning mode", ""),
        ("plain", "Plain text", ""), ("commandbar", "Command bar", ""), ("perch", "Open Perch", ""), ("snapleft", "Snap left", ""),
        ("snapright", "Snap right", ""), ("snapmax", "Maximise", ""), ("slot1", "Launcher 1", ""), ("slot2", "Launcher 2", ""),
        ("slot3", "Launcher 3", ""), ("slot4", "Launcher 4", ""), ("slot5", "Launcher 5", ""), ("slot6", "Launcher 6", "")
    };
    static Window? _open;
    public static void Run(string id)
    {
        switch (id)
        {
            case "screenshot": Tools.ScreenshotToShelf(); break;
            case "playpause": Svc.Media.Toggle(); break;
            case "next": Hooks.Press(0xB0); break;
            case "prev": Hooks.Press(0xB1); break;
            case "mute": Svc.Mic.Muted = !Svc.Mic.Muted; Svc.Notices.Add(Svc.Mic.Muted ? "Microphone muted" : "Microphone on", "", Svc.Mic.Muted ? "" : ""); break;
            case "colour": Tools.PickColour(); break;
            case "copytext": Tools.GrabText(); break;
            case "lock": Tools.Lock(); break;
            case "desktop": Hooks.Press(0x5B, 0x44); break;
            case "taskview": Hooks.Press(0x5B, 0x09); break;
            case "awake": Tools.CycleAwake(); break;
            case "clean": CleaningMode.Start(); break;
            case "plain": Tools.PlainClipboard(); break;
            case "commandbar": CommandBar.Toggle(); break;
            case "perch": IslandWindow.Instance.TogglePinned(); break;
            case "snapleft": Snap.To("left"); break;
            case "snapright": Snap.To("right"); break;
            case "snapmax": Snap.To("max"); break;
            default:
                if (id.StartsWith("slot") && id.Length == 5)
                {
                    int n = id[4] - '1'; var slots = Settings.Current.Slots;
                    if (n >= 0 && n < slots.Count && slots[n].Length > 0 && File.Exists(slots[n])) Svc.Open(slots[n]);
                }
                break;
        }
    }

    public static void Toggle()
    {
        if (_open != null) { _open.Close(); return; }
        var items = Settings.Current.RadialItems.Select(id => Catalog.FirstOrDefault(c => c.Id == id)).Where(c => c.Id != null).ToList();
        if (items.Count == 0) return;
        const double size = 330, ring = 108, btn = 54;
        var canvas = new Canvas { Width = size, Height = size, Background = Brushes.Transparent };
        var centre = new Border { Width = 96, Height = 96, CornerRadius = new CornerRadius(48), Background = B("#B3141416"), BorderBrush = B("#33FFFFFF"), BorderThickness = new Thickness(1) };
        var label = T("Choose", 11.5, "#FFFFFF", true); label.TextAlignment = TextAlignment.Center; label.TextWrapping = TextWrapping.Wrap; label.HorizontalAlignment = HorizontalAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(8);
        centre.Child = label;
        Canvas.SetLeft(centre, size / 2 - 48); Canvas.SetTop(centre, size / 2 - 48); canvas.Children.Add(centre);
        Window win = null!;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i]; double a = -Math.PI / 2 + i * 2 * Math.PI / items.Count;
            var scale = new ScaleTransform(1, 1, btn / 2, btn / 2);
            var b = new Border
            {
                Width = btn, Height = btn, CornerRadius = new CornerRadius(btn / 2), Background = B("#D91C1C1E"), BorderBrush = B("#3DFFFFFF"), BorderThickness = new Thickness(1),
                Child = Ico(it.Glyph, 18, "#FFFFFF"), RenderTransform = scale, Cursor = System.Windows.Input.Cursors.Hand
            };
            b.MouseEnter += (_, _) => { b.Background = B("#F02C2C2E"); b.BorderBrush = B(UI.Accent); scale.ScaleX = scale.ScaleY = 1.12; label.Text = it.Label; };
            b.MouseLeave += (_, _) => { b.Background = B("#D91C1C1E"); b.BorderBrush = B("#3DFFFFFF"); scale.ScaleX = scale.ScaleY = 1; label.Text = "Choose"; };
            b.MouseLeftButtonUp += (_, _) => { win.Close(); Svc.Ui(() => Run(it.Id)); };
            Canvas.SetLeft(b, size / 2 + Math.Cos(a) * ring - btn / 2); Canvas.SetTop(b, size / 2 + Math.Sin(a) * ring - btn / 2); canvas.Children.Add(b);
        }
        win = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true, ResizeMode = ResizeMode.NoResize,
            Width = size, Height = size, Content = canvas, WindowStartupLocation = WindowStartupLocation.Manual
        };
        Native.GetCursorPos(out var pt);
        var scr = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(pt.X, pt.Y)).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow ?? new Window());
        double wpx = size * dpi.DpiScaleX;
        win.Left = Math.Clamp(pt.X - wpx / 2, scr.Left, scr.Right - wpx) / dpi.DpiScaleX; win.Top = Math.Clamp(pt.Y - wpx / 2, scr.Top, scr.Bottom - wpx) / dpi.DpiScaleY;
        win.Deactivated += (_, _) => { try { win.Close(); } catch { } };
        win.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) win.Close(); };
        win.Closed += (_, _) => _open = null;
        win.Opacity = 0;
        win.Loaded += (_, _) => win.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        _open = win; win.Show(); win.Activate();
    }
}
