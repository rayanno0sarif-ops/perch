using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static Perch.UI;

namespace Perch;

// A searchable list of the programs on this PC, used to fill the launcher slots on the Tools tab.
// The list is built from the Start menu, the App Paths registry keys and the usual install folders,
// so it holds the apps people actually run rather than every helper .exe on the disk.
public class AppPicker : Window
{
    // Rank 0: Start menu (the names people know), 1: registered programs, 2: found by scanning install folders
    sealed record Entry(string Name, string Path, int Rank);

    static Task<List<Entry>>? _building;
    static AppPicker? _open;
    static readonly Dictionary<string, ImageSource?> Icons = new(StringComparer.OrdinalIgnoreCase);
    public static void Warm() => _building ??= Task.Run(Build);
    // What has been found so far (empty until the first scan finishes).
    public static List<(string Name, string Path, int Rank)> Snapshot()
    {
        Warm();
        return _building!.IsCompletedSuccessfully ? _building.Result.Select(e => (e.Name, e.Path, e.Rank)).ToList() : new();
    }
    public static void Choose(Action<string> chosen)
    {
        if (_open != null) { _open.Activate(); return; }
        _open = new AppPicker(chosen); _open.Show(); _open.Activate();
    }

    // names that mark installers, updaters and crash reporters: not something you would launch on purpose
    static readonly string[] Noise = { "unins", "uninst", "uninstall", "crashpad", "crashreport", "crash_report", "bugreport", "updater", "update", "installer", "setup", "vc_redist", "vcredist", "dxsetup", "elevate", "notification_helper", "chrome_proxy", "chrome_pwa" };

    static List<Entry> Build()
    {
        var found = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, string path, int rank)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return;
                if (!found.ContainsKey(path)) found[path] = new Entry(name, path, rank);
            }
            catch { }
        }

        // 1. Start menu shortcuts: these carry the names people know ("Discord", "Notepad++")
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic? shell = shellType == null ? null : Activator.CreateInstance(shellType);
            foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.Programs) })
            {
                if (shell == null || !Directory.Exists(dir)) continue;
                foreach (var lnk in Walk(dir, 4, "*.lnk"))
                {
                    try
                    {
                        string name = System.IO.Path.GetFileNameWithoutExtension(lnk);
                        if (Noise.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase)) || name.Contains("readme", StringComparison.OrdinalIgnoreCase)) continue;
                        string target = shell.CreateShortcut(lnk).TargetPath;
                        Add(name, target, 0);
                    }
                    catch { }
                }
            }
        }
        catch { }

        // 2. programs that registered themselves for the Run box
        foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
        {
            try
            {
                using var k = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths");
                if (k == null) continue;
                foreach (var sub in k.GetSubKeyNames())
                {
                    using var s = k.OpenSubKey(sub);
                    var p = (s?.GetValue("") as string)?.Trim('"');
                    if (p != null) Add(DisplayName(p), p, 1);
                }
            }
            catch { }
        }

        // 3. install folders, a few levels deep
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System))
        };
        var systemTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "notepad.exe", "calc.exe", "mspaint.exe", "cmd.exe", "taskmgr.exe", "regedit.exe", "mstsc.exe", "snippingtool.exe", "charmap.exe", "magnify.exe", "osk.exe", "perfmon.exe", "resmon.exe", "msinfo32.exe", "eventvwr.exe", "control.exe" };
        // reading every file's description would take minutes on a games PC, so this pass uses file names and a time limit
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var root in roots)
        {
            bool sys = root.EndsWith("System32", StringComparison.OrdinalIgnoreCase);
            foreach (var exe in Walk(root, sys ? 0 : 4, "*.exe"))
            {
                string file = System.IO.Path.GetFileNameWithoutExtension(exe);
                if (sys ? !systemTools.Contains(System.IO.Path.GetFileName(exe)) : Noise.Any(n => file.Contains(n, StringComparison.OrdinalIgnoreCase))) continue;
                if (exe.Contains(@"\Common Files\", StringComparison.OrdinalIgnoreCase) || exe.Contains(@"\Windows Kits\", StringComparison.OrdinalIgnoreCase) || exe.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) continue;
                Add(System.IO.Path.GetFileNameWithoutExtension(exe), exe, 2);
                if (clock.Elapsed.TotalSeconds > 12) break;
            }
        }

        // the same program often appears from several places; keep one entry per file name and name
        return found.Values
            .GroupBy(e => (System.IO.Path.GetFileName(e.Path).ToLowerInvariant(), e.Name.ToLowerInvariant()))
            .Select(g => g.OrderBy(e => e.Path.Length).First())
            .OrderBy(e => e.Rank).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static string DisplayName(string exe)
    {
        try { var d = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileDescription; if (!string.IsNullOrWhiteSpace(d) && d.Length < 48) return d.Trim(); } catch { }
        return System.IO.Path.GetFileNameWithoutExtension(exe);
    }

    // a bounded walk that skips folders it cannot read instead of stopping
    static IEnumerable<string> Walk(string dir, int depth, string pattern)
    {
        IEnumerable<string> files = Array.Empty<string>();
        try { files = Directory.EnumerateFiles(dir, pattern); } catch { }
        foreach (var f in files) yield return f;
        if (depth <= 0) yield break;
        IEnumerable<string> subs = Array.Empty<string>();
        try { subs = Directory.EnumerateDirectories(dir); } catch { }
        foreach (var s in subs)
            foreach (var f in Walk(s, depth - 1, pattern)) yield return f;
    }

    // ───────── the window ─────────
    readonly Action<string> _chosen;
    readonly StackPanel _rows = new(); readonly TextBlock _count; readonly TextBox _search;
    List<Entry> _all = new(); GlassBackdrop? _glass;
    const int Shown = 60;

    AppPicker(Action<string> chosen)
    {
        _chosen = chosen;
        Title = "Choose an app"; Width = 460; Height = 600; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false; Topmost = true; UseLayoutRounding = true;
        IslandWindow.Instance.Hold(true);

        var title = T("Choose an app", 13, "#FFFFFF", true);
        var close = IconBtn("", Close, 10, "#C7C7CC", 30);
        var head = MakeGrid("*,auto", "auto"); head.Children.Add(title.At(0, 0)); head.Children.Add(close.At(0, 1));
        head.Margin = new Thickness(20, 14, 14, 6); head.Background = Brushes.Transparent;
        head.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        _search = new TextBox { Background = Brushes.Transparent, Foreground = Brushes.White, CaretBrush = Brushes.White, BorderThickness = new Thickness(0), FontFamily = Font, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        var hint = T("Search your apps", 12.5, Dim); hint.IsHitTestVisible = false;
        var sg = new Grid(); sg.Children.Add(hint); sg.Children.Add(_search);
        _search.TextChanged += (_, _) => { hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Fill(); };
        var box = new Border { CornerRadius = new CornerRadius(10), BorderBrush = Glass.CardRim, BorderThickness = new Thickness(1), Background = B("#14FFFFFF"), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(18, 0, 18, 8), Child = Row(Ico("", 12, Dim).M(0, 0, 9), sg) };

        var scroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Padding = new Thickness(10, 0, 10, 0) };
        scroll.PreviewMouseWheel += (_, e) => { scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta * 0.8); e.Handled = true; };

        _count = T("Looking through your programs…", 10.5, Dim); _count.Margin = new Thickness(2, 0, 0, 0);
        var browse = Pill("Browse for something else…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Choose an app or file", Filter = "Apps and shortcuts|*.exe;*.lnk;*.url|Everything|*.*" };
            if (dlg.ShowDialog(this) == true) { _chosen(dlg.FileName); Close(); }
        }, false, "");
        var foot = MakeGrid("*,auto", "auto"); foot.Children.Add(_count.At(0, 0)); foot.Children.Add(browse.At(0, 1)); foot.Margin = new Thickness(20, 10, 18, 16);

        var root = MakeGrid("*", "auto,auto,*,auto");
        root.Children.Add(head.At(0, 0)); root.Children.Add(box.At(1, 0)); root.Children.Add(scroll.At(2, 0)); root.Children.Add(foot.At(3, 0));
        var sheen = new Border { Height = 110, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Background = Glass.Sheen, CornerRadius = new CornerRadius(13, 13, 0, 0), Opacity = 0.6, Visibility = Glass.On ? Visibility.Visible : Visibility.Collapsed };
        var rootWithSheen = new Grid(); rootWithSheen.Children.Add(sheen); rootWithSheen.Children.Add(root);
        Content = new Border
        {
            Background = Glass.Panel, CornerRadius = new CornerRadius(14), BorderBrush = Glass.On ? Glass.Rim : B("#2C2C2E"), BorderThickness = new Thickness(1), Margin = new Thickness(18), Child = rootWithSheen,
            Effect = Glass.On ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Opacity = 0.5, Color = Colors.Black }
        };
        SourceInitialized += (_, _) => { _glass = new GlassBackdrop(this) { Reach = 0.75f }; _glass.SetShape(18, 18, Width - 36, Height - 36, 14); _glass.Follow(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => { _open = null; IslandWindow.Instance.Hold(false); };
        Loaded += async (_, _) =>
        {
            Activate(); _search.Focus();
            Warm();
            _all = await _building!;
            Fill();
        };
    }

    void Fill()
    {
        _rows.Children.Clear();
        string q = _search.Text.Trim();
        // with nothing typed, only the programs you would recognise are listed; searching looks at every .exe that was found
        var hits = (q.Length == 0 ? _all.Where(e => e.Rank < 2).ToList() : _all
            .Where(e => e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || System.IO.Path.GetFileName(e.Path).Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(e => e.Rank).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList()).ToList();
        int more = _all.Count(e => e.Rank == 2);
        _count.Text = _all.Count == 0 ? "Looking through your programs…" : hits.Count == 0 ? "Nothing matches. Try Browse." : q.Length == 0 ? $"{hits.Count} apps. Search to look through {more} more .exe files." : hits.Count > Shown ? $"Showing {Shown} of {hits.Count}. Type more to narrow it." : $"{hits.Count} found";
        foreach (var e in hits.Take(Shown))
        {
            var entry = e;
            var icon = new Image { Width = 26, Height = 26, VerticalAlignment = VerticalAlignment.Center };
            if (Icons.TryGetValue(entry.Path, out var cached)) icon.Source = cached;
            else Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { try { Icons[entry.Path] = Native.FileIcon(entry.Path); } catch { Icons[entry.Path] = null; } icon.Source = Icons[entry.Path]; });
            var name = T(entry.Name, 12, "#FFFFFF", true);
            var path = T(entry.Path, 10, Dim); path.TextTrimming = TextTrimming.CharacterEllipsis;
            var row = MakeGrid("34,*", "auto");
            row.Children.Add(icon.At(0, 0)); row.Children.Add(Col(name, path).At(0, 1));
            var b = Btn(row, () => { _chosen(entry.Path); Close(); }, "#00FFFFFF", 10, new Thickness(8, 6, 8, 6));
            b.ToolTip = entry.Path;
            _rows.Children.Add(b);
        }
    }
}
