using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static Perch.UI;

namespace Perch;

// ───────────────────────── Open apps ─────────────────────────
// Every app with a window, with a Close button that asks it to close the polite way (it may offer to save first).
public class AppsTab : TabBase
{
    public override string Id => "apps"; public override string Name => "Apps"; public override string Glyph => "";
    public override double Height => 262;
    readonly StackPanel _rows = new(); string _sig = ""; int _n;

    protected override FrameworkElement Build()
    {
        var scroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        scroll.PreviewMouseWheel += (_, e) => { scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta * 0.6); e.Handled = true; };
        return Box(scroll, "#00FFFFFF", 0, new Thickness(0)).With(b => b.Margin = new Thickness(14, 4, 14, 12));
    }
    public override void Shown() { Refresh(true); }
    public override void Tick() { if (++_n % 4 == 0) Refresh(false); }

    sealed record Win(int Pid, string Name, string Title, string? Path, double Mb);
    static List<Win> Running()
    {
        var list = new List<Win>(); int me = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == me || p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle)) { p.Dispose(); continue; }
                string n = p.ProcessName;
                if (n is "explorer" or "ApplicationFrameHost" or "TextInputHost" or "SystemSettings" or "ShellExperienceHost" or "StartMenuExperienceHost" or "SearchHost") { p.Dispose(); continue; }
                string? path = null; try { path = p.MainModule?.FileName; } catch { }
                string name = n;
                if (path != null) { try { var d = FileVersionInfo.GetVersionInfo(path).FileDescription; if (!string.IsNullOrWhiteSpace(d) && d.Length < 40) name = d.Trim(); } catch { } }
                list.Add(new Win(p.Id, name, p.MainWindowTitle, path, p.WorkingSet64 / 1048576.0));
            }
            catch { }
            finally { p.Dispose(); }
        }
        return list.OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    void Refresh(bool force)
    {
        var apps = Running();
        string sig = string.Join("|", apps.Select(a => a.Pid));
        if (!force && sig == _sig) return;
        _sig = sig; _rows.Children.Clear();
        if (apps.Count == 0) { _rows.Children.Add(T("No other apps have a window open.", 11, Dim)); return; }
        foreach (var a in apps)
        {
            var app = a;
            UIElement icon = app.Path != null && Native.FileIcon(app.Path) is { } src ? new Image { Source = src, Width = 22, Height = 22 } : Ico("", 13, "#C7C7CC");
            var name = T(app.Name, 12, "#FFFFFF", true);
            var title = T(app.Title, 10, Dim); title.TextTrimming = TextTrimming.CharacterEllipsis;
            var mem = T(app.Mb >= 1024 ? $"{app.Mb / 1024:0.0} GB" : $"{app.Mb:0} MB", 10.5, Dim, false, true); mem.Margin = new Thickness(8, 0, 8, 0);
            var close = Btn(T("Close", 10.5, "#FFFFFF", true), () => Close(app.Pid), Card, 9, new Thickness(10, 5, 10, 5));
            close.ToolTip = "Ask " + app.Name + " to close. It may ask whether to save first.";
            var row = MakeGrid("30,*,auto,auto", "auto");
            row.Children.Add(new Border { Width = 22, Child = icon, VerticalAlignment = VerticalAlignment.Center }.At(0, 0));
            row.Children.Add(Col(name, title).At(0, 1)); row.Children.Add(mem.At(0, 2)); row.Children.Add(close.At(0, 3));
            row.Margin = new Thickness(0, 0, 0, 9);
            _rows.Children.Add(row);
        }
    }
    void Close(int pid)
    {
        try { using var p = Process.GetProcessById(pid); p.CloseMainWindow(); } catch { }
        _sig = ""; // list again on the next tick
    }
}

// ───────────────────────── Window snapping ─────────────────────────
public static class Snap
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);

    static readonly Dictionary<IntPtr, (string Mode, int Step)> _cycle = new();
    static readonly Dictionary<IntPtr, RECT> _saved = new(), _snapped = new(); static readonly HashSet<IntPtr> _wasMax = new();
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    // mode: left, right, top, bottom, max, center, tl, tr, bl, br, thirdl, thirdc, thirdr, twothirdsl, twothirdsr, nextdisplay, restore
    public static void To(string mode, IntPtr target = default)
    {
        try { Move(mode, target); } catch { }
    }
    static void Move(string mode, IntPtr target)
    {
        var h = target != IntPtr.Zero ? target : GetForegroundWindow();
        if (h == IntPtr.Zero) return;
        GetWindowThreadProcessId(h, out uint pid);
        if (pid == (uint)Environment.ProcessId) return; // never move Perch's own windows
        var sb = new System.Text.StringBuilder(64); GetClassName(h, sb, 64);
        if (sb.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return;
        var scr = System.Windows.Forms.Screen.FromHandle(h); var wa = scr.WorkingArea;
        if (Settings.Current.LayoutIgnoredApps.Count > 0 && Settings.Current.LayoutIgnoredApps.Any(x => x.Equals(System.IO.Path.GetFileName(WinList.PathOf(pid)), StringComparison.OrdinalIgnoreCase))) return;
        if (mode == "restore")
        {
            if (_saved.TryGetValue(h, out var sv))
            {
                ShowWindow(h, 9);
                if (_wasMax.Remove(h)) ShowWindow(h, 3); // it was maximised before: maximise it again
                else SetWindowPos(h, IntPtr.Zero, sv.L, sv.T, sv.R - sv.L, sv.B - sv.T, 0x0004 | 0x0010);
                _saved.Remove(h); _snapped.Remove(h);
            }
            return;
        }
        bool zoomed = IsZoomed(h);
        if (mode is "nextdisplay" or "prevdisplay") ShowWindow(h, 9); // a maximised window reports the screen's size; measure the restored one
        GetWindowRect(h, out var before);
        // remember where it was before the first snap, so it can be put back; snapping again does not overwrite that
        if (!_snapped.TryGetValue(h, out var last) || last.L != before.L || last.T != before.T || last.R != before.R || last.B != before.B) { _saved[h] = before; if (zoomed) _wasMax.Add(h); else _wasMax.Remove(h); }
        if (mode == "max") { ShowWindow(h, 3); return; }
        System.Drawing.Rectangle r;
        bool again = _snapped.TryGetValue(h, out var l2) && l2.L == before.L && l2.T == before.T && l2.R == before.R && l2.B == before.B;
        if (mode is "nextdisplay" or "prevdisplay")
        {
            var all = System.Windows.Forms.Screen.AllScreens;
            if (all.Length < 2) return;
            int stepDir = mode == "nextdisplay" ? 1 : all.Length - 1;
            var to = all[(Array.FindIndex(all, x => x.DeviceName == scr.DeviceName) + stepDir) % all.Length].WorkingArea;
            double fx = (double)(before.L - wa.Left) / wa.Width, fy = (double)(before.T - wa.Top) / wa.Height, fw = (double)(before.R - before.L) / wa.Width, fh = (double)(before.B - before.T) / wa.Height;
            r = new((int)(to.Left + fx * to.Width), (int)(to.Top + fy * to.Height), (int)(fw * to.Width), (int)(fh * to.Height));
        }
        else if (mode == "marginmax") r = Layout.Margin(wa);
        else if (mode == "fullscreen") r = scr.Bounds;
        else if (Settings.Current.LayoutSideCycle && mode is "left" or "right")
        {
            int step = _cycle.TryGetValue(h, out var cs) && cs.Mode == mode && again ? (cs.Step + 1) % 3 : 0; _cycle[h] = (mode, step);
            double wd = step == 0 ? .5 : step == 1 ? 2.0 / 3 : 1.0 / 3;
            r = Layout.Rect(wa, mode == "left" ? (0, 0, wd, 1) : (1 - wd, 0, wd, 1));
        }
        else if (Layout.Frac(mode, out var fr)) r = Layout.Rect(wa, fr);
        else r = mode switch
        {
            "left" => new(wa.Left, wa.Top, wa.Width / 2, wa.Height),
            "right" => new(wa.Left + wa.Width / 2, wa.Top, wa.Width - wa.Width / 2, wa.Height),
            "top" => new(wa.Left, wa.Top, wa.Width, wa.Height / 2),
            "bottom" => new(wa.Left, wa.Top + wa.Height / 2, wa.Width, wa.Height - wa.Height / 2),
            "tl" => new(wa.Left, wa.Top, wa.Width / 2, wa.Height / 2),
            "tr" => new(wa.Left + wa.Width / 2, wa.Top, wa.Width - wa.Width / 2, wa.Height / 2),
            "bl" => new(wa.Left, wa.Top + wa.Height / 2, wa.Width / 2, wa.Height - wa.Height / 2),
            "br" => new(wa.Left + wa.Width / 2, wa.Top + wa.Height / 2, wa.Width - wa.Width / 2, wa.Height - wa.Height / 2),
            "thirdl" => new(wa.Left, wa.Top, wa.Width / 3, wa.Height),
            "thirdc" => new(wa.Left + wa.Width / 3, wa.Top, wa.Width / 3, wa.Height),
            "thirdr" => new(wa.Left + wa.Width * 2 / 3, wa.Top, wa.Width - wa.Width * 2 / 3, wa.Height),
            "twothirdsl" => new(wa.Left, wa.Top, wa.Width * 2 / 3, wa.Height),
            "twothirdsr" => new(wa.Left + wa.Width / 3, wa.Top, wa.Width - wa.Width / 3, wa.Height),
            _ => new(wa.Left + wa.Width / 6, wa.Top + wa.Height / 6, wa.Width * 2 / 3, wa.Height * 2 / 3),
        };
        ShowWindow(h, 9); // restore first: a maximised window cannot be moved
        // windows have an invisible border; the visible frame is what should line up with the screen edges
        GetWindowRect(h, out var outer);
        int l = 0, t = 0, rr = 0, b = 0;
        if (DwmGetWindowAttribute(h, 9, out var ext, Marshal.SizeOf<RECT>()) == 0) { l = ext.L - outer.L; t = ext.T - outer.T; rr = outer.R - ext.R; b = outer.B - ext.B; }
        SetWindowPos(h, IntPtr.Zero, r.X - l, r.Y - t, r.Width + l + rr, r.Height + t + b, 0x0004 | 0x0010); // no z-order change, no activation
        GetWindowRect(h, out var after); _snapped[h] = after;
    }
}

// ───────────────────────── Command bar ─────────────────────────
// One field to find an app, do a sum or a conversion, find a clip, or search the web. Enter runs the highlighted row.
public class CommandBar : Window
{
    static CommandBar? _open;
    public static void Toggle() { if (_open != null) { _open.Close(); return; } _open = new CommandBar(); _open.Show(); _open.Activate(); }

    sealed record Item(string Glyph, string Title, string Sub, Action Run);
    readonly TextBox _box; readonly StackPanel _list = new(); List<Item> _items = new(); int _sel; GlassBackdrop? _glass;

    CommandBar()
    {
        Title = "Perch command bar"; Width = 540; Height = 420; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; UseLayoutRounding = true;
        var wa = SystemParameters.WorkArea; Left = wa.Left + (wa.Width - Width) / 2; Top = wa.Top + wa.Height * 0.18;
        AppPicker.Warm();

        _box = new TextBox { Background = Brushes.Transparent, Foreground = Brushes.White, CaretBrush = Brushes.White, BorderThickness = new Thickness(0), FontFamily = Display, FontSize = 17, VerticalAlignment = VerticalAlignment.Center };
        var hint = T("Search apps, calculate, convert, find a clip…", 17, Dim); hint.IsHitTestVisible = false; hint.FontFamily = Display;
        var field = new Grid(); field.Children.Add(hint); field.Children.Add(_box);
        _box.TextChanged += (_, _) => { hint.Visibility = _box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; _sel = 0; Fill(); };
        var top = MakeGrid("auto,*", "auto"); top.Children.Add(Ico("", 16, Dim).M(0, 0, 12).At(0, 0)); top.Children.Add(field.At(0, 1)); top.Margin = new Thickness(22, 18, 22, 12);
        var line = new Border { Height = 1, Background = B(Line) };
        var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Padding = new Thickness(10, 8, 10, 10) };
        scroll.PreviewMouseWheel += (_, e) => { scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta * 0.8); e.Handled = true; };
        var root = MakeGrid("*", "auto,auto,*"); root.Children.Add(top.At(0, 0)); root.Children.Add(line.At(1, 0)); root.Children.Add(scroll.At(2, 0));
        Content = new Border { Background = Glass.Panel, CornerRadius = new CornerRadius(16), BorderBrush = Glass.On ? Glass.Rim : B("#2C2C2E"), BorderThickness = new Thickness(1), Margin = new Thickness(18), Child = root,
            Effect = Glass.On ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Opacity = 0.5, Color = Colors.Black } };
        SourceInitialized += (_, _) => { _glass = new GlassBackdrop(this) { Reach = 0.75f }; _glass.SetShape(18, 18, Width - 36, Height - 36, 16); _glass.Follow(); };
        Loaded += (_, _) => { Activate(); _box.Focus(); Fill(); };
        Deactivated += (_, _) => { if (_open == this) Close(); };
        Closed += (_, _) => _open = null;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Down) { Move(1); e.Handled = true; }
            else if (e.Key == Key.Up) { Move(-1); e.Handled = true; }
            else if (e.Key == Key.Enter) { RunSelected(); e.Handled = true; }
        };
    }
    void Move(int d) { if (_items.Count == 0) return; _sel = (_sel + d + _items.Count) % _items.Count; Fill(false); }
    void RunSelected() { if (_sel < _items.Count) { var run = _items[_sel].Run; Close(); try { run(); } catch { } } }

    void Fill(bool rebuild = true)
    {
        if (rebuild) _items = Build(_box.Text.Trim());
        _list.Children.Clear();
        if (_items.Count == 0) { _list.Children.Add(T("Type to search.", 11.5, Dim).M(10, 8, 0, 0)); return; }
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i]; int idx = i;
            var row = MakeGrid("34,*", "auto");
            row.Children.Add(Ico(it.Glyph, 15, i == _sel ? "#FFFFFF" : "#C7C7CC").At(0, 0));
            row.Children.Add(Col(T(it.Title, 13, "#FFFFFF", true), T(it.Sub, 10.5, Dim)).At(0, 1));
            var b = Btn(row, () => { _sel = idx; RunSelected(); }, i == _sel ? "#33FFFFFF" : "#00FFFFFF", 11, new Thickness(10, 7, 10, 7));
            _list.Children.Add(b);
        }
    }

    List<Item> Build(string q)
    {
        var items = new List<Item>();
        if (q.Length == 0) return items;
        var calc = Calc.Evaluate(q);
        if (calc != null) items.Add(new Item("", "= " + calc, "Press Enter to copy the answer", () => { try { Clipboard.SetText(calc); Svc.Notices.Add("Result copied", calc, ""); } catch { } }));
        foreach (var a in AppPicker.Snapshot().Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || System.IO.Path.GetFileName(a.Path).Contains(q, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(a => a.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(a => a.Rank).ThenBy(a => a.Name).Take(5))
        {
            var app = a; items.Add(new Item("", app.Name, app.Path, () => Svc.Open(app.Path)));
        }
        foreach (var c in Svc.Clip.Entries.Where(e => e.Text != null && e.Text.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(3))
        {
            var clip = c; string one = clip.Text!.Replace('\r', ' ').Replace('\n', ' ').Trim();
            items.Add(new Item("", one.Length > 56 ? one[..55] + "…" : one, "From your clipboard history. Enter copies it again.", () => Svc.Clip.CopyBack(clip)));
        }
        string lower = q.ToLowerInvariant();
        if ("settings".StartsWith(lower) || "preferences".StartsWith(lower)) items.Add(new Item("", "Perch settings", "Open the settings window", () => SettingsWindow.Open()));
        if ("lock".StartsWith(lower)) items.Add(new Item("", "Lock this PC", "", Tools.Lock));
        foreach (var x in CommandBarExtras.Get(q)) { var xx = x; items.Add(new Item(xx.Glyph, xx.Title, xx.Sub, () => { CommandBarExtras.Used(xx.Title); xx.Run(); })); }
        items.Add(new Item("", "Search the web for “" + q + "”", "Opens your default browser. Nothing is sent until you press Enter.", () => Svc.Open("https://www.bing.com/search?q=" + Uri.EscapeDataString(q))));
        return items;
    }
}
