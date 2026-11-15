using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Perch.UI;

namespace Perch;

public partial class Settings
{
    public List<string> WallpaperFolders { get; set; } = new();
    public string CleanerSchedule { get; set; } = "off";        // off | daily | weekly
    public int CleanerHour { get; set; } = 3;
    public List<string> CleanerCats { get; set; } = new() { "temp", "thumbs", "crash" };
    public bool CleanerNotify { get; set; } = true;
    public int CleanerScreenshotDays { get; set; }              // screenshots older than this go to the Recycle Bin when the cleaner runs; 0 leaves them
    public string CleanerLastRun { get; set; } = "";
    public string CleanerLastResult { get; set; } = "";
    public bool DiskTrashImage { get; set; }                    // after ejecting a disk image, move the image file to the Recycle Bin
    public bool DiskWatch { get; set; } = true;                 // say so when a disk image gets mounted
    public bool WaAuto { get; set; }                            // send old messaging downloads to the Recycle Bin once a day
    public int WaDays { get; set; } = 30;
    public List<string> WaTypes { get; set; } = new() { "image", "video", "audio" };
    public string WaFolder { get; set; } = "";                  // empty is the Downloads folder
    public bool WaOrganize { get; set; }                        // move stable downloads into one folder
    public string WaDest { get; set; } = "";
    public string WaLayout { get; set; } = "type";              // flat | type | month
    public int WaDelayMin { get; set; } = 10;
    public string WaDup { get; set; } = "trash";                // trash | keep | replace
    public string WaLastRun { get; set; } = "";
    public string WaLastResult { get; set; } = "";
    public string WaUndo { get; set; } = "";                    // the last organisation, as from|to lines, for Undo
}

public partial class ManageTab
{
    static string Sz(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):0.0} GB" : b >= 1 << 20 ? $"{b / (double)(1 << 20):0.#} MB" : $"{Math.Max(0, b) / 1024.0:0} KB";
    static void Recycle(string file) => Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(file, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    static Border Check(bool on, Action<bool> set, string label, string? tip = null)
    {
        bool v = on;
        var tick = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5), BorderBrush = B(on ? "#FFFFFF" : "#636366"), BorderThickness = new Thickness(1.4), Background = on ? B("#FFFFFF") : Brushes.Transparent };
        if (on) tick.Child = Ico("\uE73E", 11, "#000000").With(x => { x.HorizontalAlignment = HorizontalAlignment.Center; x.VerticalAlignment = VerticalAlignment.Center; });
        var b = Btn(Row(tick.M(0, 0, 10).With(x => x.VerticalAlignment = VerticalAlignment.Center), T(label, 11.5, "#FFFFFF", true).With(x => x.VerticalAlignment = VerticalAlignment.Center)), () => { v = !v; set(v); }, Card, 11, new Thickness(10, 7, 12, 7));
        b.Margin = new Thickness(0, 0, 6, 6); if (tip != null) b.ToolTip = tip; return b;
    }

    // ───────── Wallpaper ─────────
    static readonly string[] ImgExt = { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".jfif" };
    string _wpFilter = "All"; int _wpShown = 24;
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern bool SystemParametersInfo(uint a, uint p, string v, uint f);
    static List<string> WallpaperFiles(string filter)
    {
        var dirs = new List<string>();
        if (filter != "Windows") { dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)); dirs.AddRange(Settings.Current.WallpaperFolders); }
        if (filter != "Yours") { dirs.Add(@"C:\Windows\Web\Wallpaper"); dirs.Add(@"C:\Windows\Web\4K\Wallpaper"); dirs.Add(@"C:\Windows\Web\Screen"); }
        var res = new List<string>();
        foreach (var d in dirs.Distinct())
        {
            try
            {
                if (File.Exists(d)) { res.Add(d); continue; }
                if (!Directory.Exists(d)) continue;
                var depth = d.Contains("Pictures", StringComparison.OrdinalIgnoreCase) ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
                res.AddRange(Directory.EnumerateFiles(d, "*", new EnumerationOptions { RecurseSubdirectories = depth == SearchOption.AllDirectories, IgnoreInaccessible = true, MaxRecursionDepth = 3 })
                    .Where(f => ImgExt.Contains(Path.GetExtension(f).ToLowerInvariant())).Take(400));
            }
            catch { }
        }
        return res.Distinct().ToList();
    }
    void WallpaperPage()
    {
        _body.Children.Add(Note("Pick a still wallpaper without opening Settings. Your Pictures folder, folders you add, and the pictures that come with Windows."));
        var bar = new WrapPanel();
        foreach (var f in new[] { "All", "Yours", "Windows" })
        {
            var name = f; bool on = name == _wpFilter;
            bar.Children.Add(Btn(T(name, 11, on ? "#000000" : "#E5E5EA", true), () => { _wpFilter = name; _wpShown = 24; Fill(); }, on ? "#FFFFFF" : Card, 9, new Thickness(10, 5, 10, 5)).M(0, 0, 5, 6));
        }
        bar.Children.Add(Pill("Add folder", () =>
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Pictures to offer as wallpaper" };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !Settings.Current.WallpaperFolders.Contains(dlg.SelectedPath)) { Settings.Current.WallpaperFolders.Add(dlg.SelectedPath); Settings.Current.Save(); Fill(); }
        }).M(6, 0, 5, 6));
        bar.Children.Add(Pill("Add picture", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Pictures|*.jpg;*.jpeg;*.png;*.bmp;*.webp" };
            if (dlg.ShowDialog() == true && !Settings.Current.WallpaperFolders.Contains(dlg.FileName)) { Settings.Current.WallpaperFolders.Add(dlg.FileName); Settings.Current.Save(); Fill(); }
        }).M(0, 0, 5, 6));
        _body.Children.Add(bar);
        var wrap = new WrapPanel(); _body.Children.Add(wrap);
        int gen = _gen;
        _ = Task.Run(() => WallpaperFiles(_wpFilter)).ContinueWith(t => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (gen != _gen) return;
            var files = t.Result;
            if (files.Count == 0) { wrap.Children.Add(Note("No pictures found here.")); return; }
            foreach (var f in files.Take(_wpShown))
            {
                var path = f; var img = new Image { Stretch = Stretch.UniformToFill };
                var cell = new Border { Width = 118, Height = 70, CornerRadius = new CornerRadius(9), Background = B("#26FFFFFF"), Margin = new Thickness(0, 0, 6, 6), Child = img, ClipToBounds = true, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = Path.GetFileName(path) };
                cell.MouseLeftButtonUp += (_, _) =>
                {
                    bool ok = SystemParametersInfo(0x0014, 0, path, 0x01 | 0x02);
                    Svc.Notices.Add(ok ? "Wallpaper changed" : "Could not change the wallpaper", Path.GetFileName(path), "");
                };
                wrap.Children.Add(cell);
                _ = Task.Run(() =>
                {
                    try
                    {
                        var bi = new BitmapImage(); bi.BeginInit(); bi.DecodePixelWidth = 200; bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(path); bi.EndInit(); bi.Freeze(); return bi;
                    }
                    catch { return null; }
                }).ContinueWith(r => Application.Current?.Dispatcher.BeginInvoke(() => { if (r.Result != null) img.Source = r.Result; }));
            }
            if (files.Count > _wpShown) wrap.Children.Add(Pill($"Show more ({files.Count - _wpShown})", () => { _wpShown += 24; Fill(); }).M(0, 4, 0, 0));
        }));
    }

    // ───────── Processes ─────────
    bool _grouped = true; string _armed = ""; DateTime _armedAt;
    static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase) { "System", "Idle", "csrss", "wininit", "winlogon", "lsass", "smss", "services", "dwm", "Registry", "fontdrvhost", "Memory Compression" };
    sealed record Proc(string Name, int Pid, double Mb, string? Path, int Count, List<int> Pids);
    void ProcessesPage()
    {
        var box = SearchBox("Filter by name or PID, then press Enter", q => Fill());
        _body.Children.Add(Row(Check(_grouped, v => { _grouped = v; Fill(); }, "Group related processes", "Helper processes are listed under the program that owns them.")));
        var list = new StackPanel(); _body.Children.Add(list);
        int gen = _gen; string q = _query;
        _ = Task.Run(() =>
        {
            var all = new List<(string Name, int Pid, double Mb, string? Path)>(); int me = Environment.ProcessId;
            foreach (var p in Process.GetProcesses())
            {
                try { if (p.Id == me) continue; string? path = null; try { path = p.MainModule?.FileName; } catch { } all.Add((p.ProcessName, p.Id, p.WorkingSet64 / 1048576.0, path)); }
                catch { }
                finally { p.Dispose(); }
            }
            var filtered = all.Where(a => q.Length == 0 || a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || a.Pid.ToString() == q).ToList();
            IEnumerable<Proc> rows = _grouped
                ? filtered.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Select(g => new Proc(g.Key, g.First().Pid, g.Sum(x => x.Mb), g.Select(x => x.Path).FirstOrDefault(x => x != null), g.Count(), g.Select(x => x.Pid).ToList()))
                : filtered.Select(a => new Proc(a.Name, a.Pid, a.Mb, a.Path, 1, new List<int> { a.Pid }));
            return rows.OrderByDescending(r => r.Mb).Take(60).ToList();
        }).ContinueWith(t => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (gen != _gen) return;
            var rows = t.Result; if (rows.Count == 0) { list.Children.Add(Note("No process matches.")); return; }
            list.Children.Add(Note($"{rows.Count} shown, biggest memory first. Ending a program loses anything unsaved in it."));
            foreach (var r in rows)
            {
                var pr = r; bool prot = Protected.Contains(pr.Name);
                string sub = (pr.Count > 1 ? $"{pr.Count} processes · " : $"PID {pr.Pid} · ") + Sz((long)(pr.Mb * 1048576));
                string key = "k:" + pr.Name + pr.Pid;
                var left = Col(T(pr.Name, 12, "#FFFFFF", true).With(x => x.TextTrimming = TextTrimming.CharacterEllipsis), T(sub, 10.5, Dim));
                var actions = Row();
                if (!prot)
                {
                    bool arm = _armed == key && (DateTime.Now - _armedAt).TotalSeconds < 4;
                    actions.Children.Add(Pill(arm ? "Sure?" : pr.Count > 1 ? "End all" : "End", () =>
                    {
                        if (_armed != key) { _armed = key; _armedAt = DateTime.Now; Fill(); return; }
                        _armed = ""; int n = 0; foreach (var pid in pr.Pids) { try { using var p = Process.GetProcessById(pid); p.Kill(); n++; } catch { } }
                        Svc.Notices.Add(n > 0 ? "Ended " + pr.Name : "Could not end " + pr.Name, n > 0 ? "" : "It may need administrator rights.", ""); Fill();
                    }, arm));
                    actions.Children.Add(Pill("Tree", () =>
                    {
                        int n = 0; foreach (var pid in pr.Pids) { try { using var p = Process.GetProcessById(pid); p.Kill(true); n++; } catch { } }
                        Svc.Notices.Add(n > 0 ? "Ended " + pr.Name + " and what it started" : "Could not end " + pr.Name, "", ""); Fill();
                    }).M(5));
                    if (pr.Path != null) actions.Children.Add(Pill("Restart", () =>
                    {
                        foreach (var pid in pr.Pids) { try { using var p = Process.GetProcessById(pid); p.Kill(); } catch { } }
                        try { Process.Start(new ProcessStartInfo(pr.Path!) { UseShellExecute = true }); } catch { }
                        Fill();
                    }).M(5));
                }
                if (pr.Path != null) actions.Children.Add(Pill("Copy path", () => { try { Clipboard.SetText(pr.Path!); } catch { } }).M(5));
                var row = MakeGrid("*,auto", "auto"); row.Children.Add(left.At(0, 0)); row.Children.Add(actions.At(0, 1));
                list.Children.Add(Box(row, Card, 12, new Thickness(11, 7, 8, 7)).M(0, 0, 0, 6));
            }
        }));
    }

    // ───────── Disk images ─────────
    string _imgPath = "", _imgDrive = "", _imgNote = "";
    static string Ps(string script)
    {
        var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command -") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
        using var p = Process.Start(psi)!; p.StandardInput.WriteLine(script); p.StandardInput.Close();
        string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(30000); return o.Trim();
    }
    static string Q(string s) => "'" + s.Replace("'", "''") + "'";
    void DisksPage()
    {
        var s = Settings.Current;
        _body.Children.Add(Note("Mount a disk image (ISO, IMG or VHD), run its installer, then eject it and optionally put the image file in the Recycle Bin."));
        _body.Children.Add(Row(Pill("Choose a disk image…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Disk images|*.iso;*.img;*.vhd;*.vhdx" };
            if (dlg.ShowDialog() != true) return;
            _imgPath = dlg.FileName; _imgNote = "Mounting…"; Fill();
            _ = Task.Run(() => Ps($"Mount-DiskImage -ImagePath {Q(_imgPath)} -ErrorAction Stop | Out-Null; (Get-DiskImage -ImagePath {Q(_imgPath)} | Get-Volume).DriveLetter"))
                .ContinueWith(t => Application.Current?.Dispatcher.BeginInvoke(() => { var d = t.Result.Trim(); _imgDrive = d.Length > 0 ? d[..1] + ":\\" : ""; _imgNote = _imgDrive.Length > 0 ? "" : "It could not be mounted."; Fill(); }));
        }, true)));
        _body.Children.Add(Check(s.DiskTrashImage, v => { s.DiskTrashImage = v; s.Save(); Fill(); }, "Move the image to the Recycle Bin after ejecting").M(0, 6, 0, 0));
        _body.Children.Add(Check(s.DiskWatch, v => { s.DiskWatch = v; s.Save(); Fill(); }, "Tell me when a disk image is mounted"));
        if (_imgNote.Length > 0) _body.Children.Add(Note(_imgNote));
        if (_imgDrive.Length > 0 && Directory.Exists(_imgDrive))
        {
            string[] names = { "setup.exe", "install.exe", "installer.exe", "autorun.exe", "start.exe" };
            var exes = Directory.EnumerateFiles(_imgDrive, "*.exe", SearchOption.TopDirectoryOnly).ToList();
            var main = exes.FirstOrDefault(f => names.Contains(Path.GetFileName(f).ToLowerInvariant())) ?? exes.FirstOrDefault();
            _body.Children.Add(Line(Path.GetFileName(_imgPath), $"Mounted as {_imgDrive}", "Open", () => Svc.Open(_imgDrive)));
            if (main != null) _body.Children.Add(Line("Run " + Path.GetFileName(main), "Opens the installer found on the image.", "Run", () => Svc.Open(main), true));
            else _body.Children.Add(Note("No installer program in the top folder of the image. Open it to look."));
            _body.Children.Add(Line("Eject", "Unmount the image" + (s.DiskTrashImage ? " and move the image file to the Recycle Bin." : "."), "Eject", () =>
            {
                string img = _imgPath; _imgDrive = ""; _imgNote = "Ejecting…"; Fill();
                _ = Task.Run(() =>
                {
                    Ps($"Dismount-DiskImage -ImagePath {Q(img)} | Out-Null");
                    if (Settings.Current.DiskTrashImage) { try { Recycle(img); } catch { } }
                }).ContinueWith(_ => Application.Current?.Dispatcher.BeginInvoke(() => { _imgPath = ""; _imgNote = Settings.Current.DiskTrashImage ? "Ejected. The image is in the Recycle Bin." : "Ejected."; Fill(); }));
            }));
        }
    }

    // ───────── Messaging downloads ─────────
    static string WaDir => Settings.Current.WaFolder.Length > 0 ? Settings.Current.WaFolder : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public static string WaKind(string file)
    {
        string e = Path.GetExtension(file).ToLowerInvariant();
        if (e is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".heic" or ".bmp") return "image";
        if (e is ".mp4" or ".mov" or ".mkv" or ".avi" or ".3gp" or ".webm") return "video";
        if (e is ".mp3" or ".m4a" or ".ogg" or ".opus" or ".wav" or ".aac" or ".amr") return "audio";
        if (e is ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".csv" or ".rtf") return "document";
        if (e is ".zip" or ".rar" or ".7z" or ".tar" or ".gz") return "archive";
        return "other";
    }
    // Files that came from a messaging app: the app's own file names, or a download mark that names it.
    public static List<(string Path, long Size, DateTime At, string Kind)> WaFiles()
    {
        var res = new List<(string, long, DateTime, string)>();
        try
        {
            if (!Directory.Exists(WaDir)) return res;
            foreach (var f in Directory.EnumerateFiles(WaDir, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var name = Path.GetFileName(f);
                    bool yes = name.StartsWith("WhatsApp ", StringComparison.OrdinalIgnoreCase) || name.StartsWith("PTT-", StringComparison.OrdinalIgnoreCase) && false;
                    if (!yes) { try { var z = File.ReadAllText(f + ":Zone.Identifier"); yes = z.Contains("whatsapp", StringComparison.OrdinalIgnoreCase); } catch { } }
                    if (!yes) continue;
                    var fi = new FileInfo(f); res.Add((f, fi.Length, fi.LastWriteTime, WaKind(f)));
                }
                catch { }
            }
        }
        catch { }
        return res;
    }
    static readonly string[] WaKinds = { "image", "video", "audio", "document", "archive", "other" };
    static readonly string[] WaLabels = { "Images", "Videos", "Audio and voice notes", "Documents", "Archives", "Other" };
    List<(string Path, long Size, DateTime At, string Kind)>? _waScan; HashSet<string> _waKeep = new();
    void DownloadsPage()
    {
        var s = Settings.Current;
        _body.Children.Add(Note("Finds files that came from WhatsApp: their own file names, or a download mark that names it. File contents and chats are never read. Everything goes to the Recycle Bin, never straight to deletion."));
        _body.Children.Add(Note("Watching " + WaDir));
        var types = new WrapPanel();
        for (int i = 0; i < WaKinds.Length; i++) { var k = WaKinds[i]; types.Children.Add(Check(s.WaTypes.Contains(k), v => { if (v) { if (!s.WaTypes.Contains(k)) s.WaTypes.Add(k); } else s.WaTypes.Remove(k); s.Save(); Fill(); }, WaLabels[i])); }
        _body.Children.Add(types);
        _body.Children.Add(Row(T("Keep for", 11.5, Dim).M(2, 6, 10, 0), Pill("−", () => { s.WaDays = Math.Max(1, s.WaDays - (s.WaDays > 14 ? 7 : 1)); s.Save(); Fill(); }), T($"{s.WaDays} days", 12, "#FFFFFF", true).M(10, 6, 10, 0), Pill("+", () => { s.WaDays = Math.Min(365, s.WaDays + (s.WaDays >= 14 ? 7 : 1)); s.Save(); Fill(); })));
        _body.Children.Add(Check(s.WaAuto, v => { s.WaAuto = v; s.Save(); Fill(); }, "Clean up automatically once a day").M(0, 8, 0, 0));
        if (s.WaLastRun.Length > 0) _body.Children.Add(Note("Last cleanup " + s.WaLastRun + ": " + s.WaLastResult));
        _body.Children.Add(Pill(_waScan == null ? "Scan now" : "Scan again", () => { _waScan = null; _waKeep.Clear(); Fill(); _ = Task.Run(WaFiles).ContinueWith(t => Application.Current?.Dispatcher.BeginInvoke(() => { _waScan = t.Result; Fill(); })); }, true).With(p => p.HorizontalAlignment = HorizontalAlignment.Left).M(0, 6, 0, 8));
        if (_waScan != null)
        {
            if (_waScan.Count == 0) _body.Children.Add(Note("No WhatsApp files found in that folder."));
            else
            {
                var due = _waScan.Where(f => s.WaTypes.Contains(f.Kind) && (DateTime.Now - f.At).TotalDays >= s.WaDays && !_waKeep.Contains(f.Path)).ToList();
                _body.Children.Add(Note($"{_waScan.Count} files · {Sz(_waScan.Sum(f => f.Size))}. {due.Count} match your rules ({Sz(due.Sum(f => f.Size))})."));
                foreach (var f in _waScan.OrderByDescending(f => f.Size).Take(40))
                {
                    var file = f; bool keep = _waKeep.Contains(f.Path);
                    _body.Children.Add(Line(Path.GetFileName(f.Path), $"{WaLabels[Array.IndexOf(WaKinds, f.Kind)]} · {Sz(f.Size)} · {(int)(DateTime.Now - f.At).TotalDays} days old", keep ? "Kept" : "Keep", () => { if (!_waKeep.Remove(file.Path)) _waKeep.Add(file.Path); Fill(); }));
                }
                if (due.Count > 0) _body.Children.Add(Pill($"Move {due.Count} to the Recycle Bin", () =>
                {
                    int n = 0; long bytes = 0; foreach (var d in due) { try { Recycle(d.Path); n++; bytes += d.Size; } catch { } }
                    s.WaLastRun = DateTime.Now.ToString("g"); s.WaLastResult = $"{n} files · {Sz(bytes)}"; s.Save();
                    Svc.Notices.Add("WhatsApp cleanup", $"{n} files ({Sz(bytes)}) moved to the Recycle Bin.", ""); _waScan = null; Fill();
                }, true).With(p => p.HorizontalAlignment = HorizontalAlignment.Left).M(0, 4, 0, 8));
            }
        }
        _body.Children.Add(T("AUTOMATIC ORGANISATION", 10, Dim, false, true).M(2, 12, 0, 6));
        _body.Children.Add(Note("Moves stable downloads into one folder and spots exact repeat downloads. WhatsApp may download a moved file again; Perch cannot stop that, but it can discard an identical extra copy."));
        _body.Children.Add(Check(s.WaOrganize, v => { s.WaOrganize = v; s.Save(); Fill(); }, "Organise automatically"));
        if (s.WaOrganize)
        {
            _body.Children.Add(Row(T("Folder structure", 11.5, Dim).M(2, 6, 10, 0), new Segmented(new[] { "None", "By type", "By month" }, s.WaLayout == "flat" ? 0 : s.WaLayout == "month" ? 2 : 1, i => { s.WaLayout = new[] { "flat", "type", "month" }[i]; s.Save(); })));
            _body.Children.Add(Row(T("A repeat download", 11.5, Dim).M(2, 8, 10, 0), new Segmented(new[] { "Recycle new", "Keep both", "Replace" }, s.WaDup == "keep" ? 1 : s.WaDup == "replace" ? 2 : 0, i => { s.WaDup = new[] { "trash", "keep", "replace" }[i]; s.Save(); })).M(0, 6, 0, 0));
            _body.Children.Add(Row(T("Wait before moving", 11.5, Dim).M(2, 8, 10, 0), Pill("−", () => { s.WaDelayMin = Math.Max(1, s.WaDelayMin - 5); s.Save(); Fill(); }), T($"{s.WaDelayMin} min", 12, "#FFFFFF", true).M(10, 8, 10, 0), Pill("+", () => { s.WaDelayMin = Math.Min(120, s.WaDelayMin + 5); s.Save(); Fill(); })));
            _body.Children.Add(Note("Destination: " + (s.WaDest.Length > 0 ? s.WaDest : Path.Combine(WaDir, "WhatsApp"))));
            _body.Children.Add(Row(Pill("Choose folder…", () => { using var dlg = new System.Windows.Forms.FolderBrowserDialog(); if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) { s.WaDest = dlg.SelectedPath; s.Save(); Fill(); } }),
                Pill("Organise now", () => { var r = Scheduled.Organise(true); Svc.Notices.Add("WhatsApp organisation", r, ""); Fill(); }).M(6), Pill("Undo the last one", () => { Svc.Notices.Add("WhatsApp organisation", Scheduled.UndoOrganise(), ""); Fill(); }).M(6)));
            if (s.WaLastResult.Length > 0 && s.WaLastRun.Length > 0 && !s.WaAuto) _body.Children.Add(Note("Last run " + s.WaLastRun));
        }
    }

    // ───────── cleaner schedule (shown under the Cleaner page) ─────────
    void CleanerSchedule()
    {
        var s = Settings.Current;
        _body.Children.Add(T("SCHEDULE", 10, Dim, false, true).M(2, 14, 0, 6));
        _body.Children.Add(Note("Cleans the categories ticked below on its own, then tells you what it freed. It only runs while Perch is running."));
        _body.Children.Add(new Segmented(new[] { "Off", "Every day", "Every week" }, s.CleanerSchedule == "daily" ? 1 : s.CleanerSchedule == "weekly" ? 2 : 0, i => { s.CleanerSchedule = new[] { "off", "daily", "weekly" }[i]; s.Save(); Fill(); }));
        if (s.CleanerSchedule != "off")
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var it in CleanItems) { var id = it.Id; wrap.Children.Add(Check(s.CleanerCats.Contains(id), v => { if (v) { if (!s.CleanerCats.Contains(id)) s.CleanerCats.Add(id); } else s.CleanerCats.Remove(id); s.Save(); Fill(); }, it.Title)); }
            _body.Children.Add(wrap);
            _body.Children.Add(Row(T("At", 11.5, Dim).M(2, 6, 10, 0), Pill("−", () => { s.CleanerHour = (s.CleanerHour + 23) % 24; s.Save(); Fill(); }), T($"{s.CleanerHour:00}:00", 12, "#FFFFFF", true).M(10, 6, 10, 0), Pill("+", () => { s.CleanerHour = (s.CleanerHour + 1) % 24; s.Save(); Fill(); })));
            _body.Children.Add(Row(T("Also old screenshots", 11.5, Dim).M(2, 8, 10, 0), Pill("−", () => { s.CleanerScreenshotDays = Math.Max(0, s.CleanerScreenshotDays - 15); s.Save(); Fill(); }), T(s.CleanerScreenshotDays == 0 ? "never" : $"after {s.CleanerScreenshotDays} days", 12, "#FFFFFF", true).M(10, 8, 10, 0), Pill("+", () => { s.CleanerScreenshotDays = Math.Min(365, s.CleanerScreenshotDays + 15); s.Save(); Fill(); })));
            _body.Children.Add(Check(s.CleanerNotify, v => { s.CleanerNotify = v; s.Save(); }, "Tell me when it has run"));
        }
        if (s.CleanerLastRun.Length > 0) _body.Children.Add(Note("Last automatic run " + s.CleanerLastRun + ": " + s.CleanerLastResult));
    }
}

// Things that run by themselves: the cleaner schedule, messaging downloads, and a mounted disk image.
public static class Scheduled
{
    static readonly HashSet<string> _low = new(); static int _n; static bool _busy; static readonly HashSet<string> _drives = new(); static bool _drivesInit;
    public static void Tick(Settings s)
    {
        _n++; Tools2.MusicTick(); MonitorAlerts.Tick(s);
        if (s.AccessoryAlerts && _n % 600 == 5) Task.Run(() => { foreach (var d in ManageTab.ReadDevices().Where(d => d.State == "connected" && d.Battery >= 0)) { if (d.Battery <= 20 && _low.Add(d.Name)) Svc.Ui(() => Svc.Notices.Add("Low battery: " + d.Name, d.Battery + "% left.", "\uE850")); else if (d.Battery > 25) _low.Remove(d.Name); } });
        try
        {
            if (_n % 4 == 0 && s.DiskWatch) WatchDrives();
            if (_busy || _n % 30 != 7) return;
            _busy = true;
            Task.Run(() =>
            {
                try
                {
                    if (s.CleanerSchedule != "off") RunCleaner(s);
                    if (s.WaAuto) RunWaCleanup(s);
                    if (s.WaOrganize) Organise(false);
                }
                catch { }
                finally { _busy = false; }
            });
        }
        catch { }
    }
    static void WatchDrives()
    {
        var now = new HashSet<string>();
        foreach (var d in DriveInfo.GetDrives()) { try { if (d.DriveType == DriveType.CDRom && d.IsReady) now.Add(d.Name); } catch { } }
        if (_drivesInit) foreach (var d in now.Where(x => !_drives.Contains(x))) Svc.Notices.Add("Disk image mounted", $"{d} is ready. Open Manage, then Disks, to install from it and eject it.", "\uE958");
        _drives.Clear(); foreach (var d in now) _drives.Add(d); _drivesInit = true;
    }
    static void RunCleaner(Settings s)
    {
        var now = DateTime.Now; DateTime.TryParse(s.CleanerLastRun, out var last);
        bool due = s.CleanerSchedule == "daily" ? last.Date < now.Date : (now - last).TotalDays >= 7 || last == default;
        if (!due || now.Hour < s.CleanerHour) return;
        int n = 0; long bytes = 0;
        foreach (var it in ManageTab.CleanItems.Where(i => s.CleanerCats.Contains(i.Id)))
            foreach (var f in ManageTab.Files(it).ToList()) { try { long len = new FileInfo(f).Length; File.Delete(f); n++; bytes += len; } catch { } }
        int shots = 0;
        if (s.CleanerScreenshotDays > 0)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
            try
            {
                if (Directory.Exists(dir))
                    foreach (var f in Directory.EnumerateFiles(dir).Where(f => (now - File.GetLastWriteTime(f)).TotalDays >= s.CleanerScreenshotDays).ToList())
                    { try { long len = new FileInfo(f).Length; Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(f, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); shots++; bytes += len; } catch { } }
            }
            catch { }
        }
        s.CleanerLastRun = now.ToString("g"); s.CleanerLastResult = $"{n} files{(shots > 0 ? $" and {shots} old screenshots" : "")}, {bytes / 1048576.0:0.#} MB"; s.Save();
        if (s.CleanerNotify) Svc.Ui(() => Svc.Notices.Add("Cleaner ran", s.CleanerLastResult, ""));
    }
    static void RunWaCleanup(Settings s)
    {
        DateTime.TryParse(s.WaLastRun, out var last); if (last.Date >= DateTime.Now.Date && s.WaLastResult.Length > 0 && !s.WaLastResult.StartsWith("organised")) return;
        int n = 0; long bytes = 0;
        foreach (var f in ManageTab.WaFiles().Where(f => s.WaTypes.Contains(f.Kind) && (DateTime.Now - f.At).TotalDays >= s.WaDays))
        { try { Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(f.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); n++; bytes += f.Size; } catch { } }
        s.WaLastRun = DateTime.Now.ToString("g"); s.WaLastResult = $"{n} files · {bytes / 1048576.0:0.#} MB"; s.Save();
        if (n > 0) Svc.Ui(() => Svc.Notices.Add("WhatsApp cleanup", $"{n} files moved to the Recycle Bin.", ""));
    }
    static string Hash(string f) { using var sha = SHA256.Create(); using var fs = File.OpenRead(f); return Convert.ToHexString(sha.ComputeHash(fs)); }
    public static string Organise(bool force)
    {
        var s = Settings.Current; string dest = s.WaDest.Length > 0 ? s.WaDest : Path.Combine(Directory.Exists(s.WaFolder) ? s.WaFolder : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "WhatsApp");
        int moved = 0, dups = 0, failed = 0; var undo = new StringBuilder();
        foreach (var f in ManageTab.WaFiles())
        {
            try
            {
                if (!force && (DateTime.Now - File.GetLastWriteTime(f.Path)).TotalMinutes < s.WaDelayMin) continue;
                if (force && (DateTime.Now - File.GetLastWriteTime(f.Path)).TotalSeconds < 30) continue; // still being written
                string sub = s.WaLayout == "type" ? f.Kind : s.WaLayout == "month" ? f.At.ToString("yyyy-MM") : "";
                string dir = Path.Combine(dest, sub); Directory.CreateDirectory(dir);
                string target = Path.Combine(dir, Path.GetFileName(f.Path));
                if (File.Exists(target))
                {
                    if (new FileInfo(target).Length == f.Size && Hash(target) == Hash(f.Path))
                    {
                        dups++;
                        if (s.WaDup == "trash") Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(f.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        else if (s.WaDup == "replace") { File.Delete(target); File.Move(f.Path, target); }
                        continue;
                    }
                    string stem = Path.GetFileNameWithoutExtension(target), ext = Path.GetExtension(target); int i = 2;
                    while (File.Exists(target)) target = Path.Combine(dir, $"{stem} ({i++}){ext}");
                }
                File.Move(f.Path, target); undo.AppendLine(f.Path + "|" + target); moved++;
            }
            catch { failed++; }
        }
        if (moved > 0) s.WaUndo = undo.ToString();
        string r = $"{moved} moved · {dups} repeats · {failed} failed"; if (moved + dups > 0 || force) { s.WaLastRun = DateTime.Now.ToString("g"); s.WaLastResult = "organised " + r; s.Save(); }
        return r;
    }
    public static string UndoOrganise()
    {
        var s = Settings.Current; int n = 0;
        foreach (var line in s.WaUndo.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Trim().Split('|'); if (p.Length != 2) continue;
            try { if (File.Exists(p[1]) && !File.Exists(p[0])) { File.Move(p[1], p[0]); n++; } } catch { }
        }
        s.WaUndo = ""; s.Save(); return n > 0 ? $"{n} files put back." : "Nothing to undo.";
    }
}
