using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static Perch.UI;

namespace Perch;

// Settings for the window features: the switcher, the extra layouts and the drag gestures. Everything is off until switched on.
public partial class Settings
{
    public bool SwitcherOn { get; set; }                       // Alt+Tab shows previews, with search
    public string SwitcherLayout { get; set; } = "previews";   // previews | icons | list
    public bool SwitcherMinimized { get; set; } = true;        // minimised windows are listed too
    public bool SwitcherCurrentDisplay { get; set; }           // only windows on the display the pointer is on
    public int SwitcherColumns { get; set; } = 4;
    public int SwitcherDelayMs { get; set; } = 120;            // a quick tap switches straight away without showing the list
    public List<string> SwitcherHiddenApps { get; set; } = new();
    public bool SwitcherSameApp { get; set; } = true;          // Alt+` cycles the windows of the program in front
    public int LayoutGap { get; set; }                         // pixels between snapped windows
    public int LayoutScreenGap { get; set; }                   // pixels between a snapped window and the screen edge
    public int LayoutMarginPct { get; set; } = 5;              // "maximise with margin"
    public bool LayoutSideCycle { get; set; }                  // pressing left or right again cycles half, two thirds, one third
    public bool GestureOn { get; set; }                        // hold a key and drag anywhere in a window to move it
    public string GestureKey { get; set; } = "alt";            // alt | ctrlalt | win
    public bool GestureRaise { get; set; } = true;
    public bool GestureEdgeSnap { get; set; } = true;          // let go near a screen edge or corner and the window snaps there
    public List<string> LayoutIgnoredApps { get; set; } = new();
    public bool DeskGesture { get; set; }                      // hold a mouse button and drag to switch virtual desktops
    public string DeskGestureButton { get; set; } = "middle";  // middle | x1 | x2
    public bool DeskFollow { get; set; } = true;               // dragging right brings the desktop on the left, like a swipe
}

public static class Layout
{
    public static readonly (string Id, string Label, string Group)[] All =
    {
        ("left", "Left half", "Halves"), ("right", "Right half", "Halves"), ("top", "Top half", "Halves"), ("bottom", "Bottom half", "Halves"), ("centerhalf", "Centre half", "Halves"),
        ("thirdl", "Left third", "Thirds"), ("thirdc", "Middle third", "Thirds"), ("thirdr", "Right third", "Thirds"),
        ("twothirdsl", "Left two thirds", "Thirds"), ("twothirdsc", "Centre two thirds", "Thirds"), ("twothirdsr", "Right two thirds", "Thirds"),
        ("vthird1", "Top third", "Thirds"), ("vthird2", "Middle row third", "Thirds"), ("vthird3", "Bottom third", "Thirds"),
        ("vtwothirdst", "Top two thirds", "Thirds"), ("vtwothirdsb", "Bottom two thirds", "Thirds"),
        ("rowq1", "Top quarter", "Quarters"), ("rowq2", "Upper middle quarter", "Quarters"), ("rowq3", "Lower middle quarter", "Quarters"), ("rowq4", "Bottom quarter", "Quarters"),
        ("colq1", "Left quarter", "Quarters"), ("colq2", "Left middle quarter", "Quarters"), ("colq3", "Right middle quarter", "Quarters"), ("colq4", "Right quarter", "Quarters"),
        ("tl6", "Top left sixth", "Sixths"), ("tc6", "Top centre sixth", "Sixths"), ("tr6", "Top right sixth", "Sixths"),
        ("bl6", "Bottom left sixth", "Sixths"), ("bc6", "Bottom centre sixth", "Sixths"), ("br6", "Bottom right sixth", "Sixths"),
        ("tl", "Top left corner", "Corners"), ("tr", "Top right corner", "Corners"), ("bl", "Bottom left corner", "Corners"), ("br", "Bottom right corner", "Corners"),
        ("max", "Maximise", "Other"), ("marginmax", "Maximise with margin", "Other"), ("center", "Centre", "Other"), ("fullscreen", "Cover the whole display", "Other"),
        ("nextdisplay", "Next display", "Other"), ("prevdisplay", "Previous display", "Other"), ("restore", "Put it back", "Other"),
    };

    // Where a mode lands, as a share of the display: x, y, width, height.
    public static bool Frac(string mode, out (double X, double Y, double W, double H) f)
    {
        const double t = 1.0 / 3, q = 0.25, s = 1.0 / 6;
        f = mode switch
        {
            "left" => (0, 0, .5, 1), "right" => (.5, 0, .5, 1), "top" => (0, 0, 1, .5), "bottom" => (0, .5, 1, .5), "centerhalf" => (.25, 0, .5, 1),
            "thirdl" => (0, 0, t, 1), "thirdc" => (t, 0, t, 1), "thirdr" => (2 * t, 0, t, 1),
            "twothirdsl" => (0, 0, 2 * t, 1), "twothirdsc" => (s, 0, 2 * t, 1), "twothirdsr" => (t, 0, 2 * t, 1),
            "vthird1" => (0, 0, 1, t), "vthird2" => (0, t, 1, t), "vthird3" => (0, 2 * t, 1, t),
            "vtwothirdst" => (0, 0, 1, 2 * t), "vtwothirdsb" => (0, t, 1, 2 * t),
            "rowq1" => (0, 0, 1, q), "rowq2" => (0, q, 1, q), "rowq3" => (0, 2 * q, 1, q), "rowq4" => (0, 3 * q, 1, q),
            "colq1" => (0, 0, q, 1), "colq2" => (q, 0, q, 1), "colq3" => (2 * q, 0, q, 1), "colq4" => (3 * q, 0, q, 1),
            "tl6" => (0, 0, t, .5), "tc6" => (t, 0, t, .5), "tr6" => (2 * t, 0, t, .5), "bl6" => (0, .5, t, .5), "bc6" => (t, .5, t, .5), "br6" => (2 * t, .5, t, .5),
            "tl" => (0, 0, .5, .5), "tr" => (.5, 0, .5, .5), "bl" => (0, .5, .5, .5), "br" => (.5, .5, .5, .5),
            "center" => (s, s, 2 * t, 2 * t),
            _ => (0, 0, 0, 0)
        };
        return f.W > 0;
    }

    // The pixel rectangle for a share of the display, with the gap to the screen edge and between windows applied.
    public static System.Drawing.Rectangle Rect(System.Drawing.Rectangle wa, (double X, double Y, double W, double H) f)
    {
        var s = Settings.Current; int sg = Math.Max(0, s.LayoutScreenGap), g = Math.Max(0, s.LayoutGap);
        int ax = wa.Left + sg, ay = wa.Top + sg, aw = wa.Width - 2 * sg, ah = wa.Height - 2 * sg;
        int x0 = ax + (int)Math.Round(aw * f.X), y0 = ay + (int)Math.Round(ah * f.Y);
        int x1 = ax + (int)Math.Round(aw * (f.X + f.W)), y1 = ay + (int)Math.Round(ah * (f.Y + f.H));
        if (f.X > 0.001) x0 += g / 2; if (f.X + f.W < 0.999) x1 -= g - g / 2;
        if (f.Y > 0.001) y0 += g / 2; if (f.Y + f.H < 0.999) y1 -= g - g / 2;
        return new System.Drawing.Rectangle(x0, y0, Math.Max(60, x1 - x0), Math.Max(40, y1 - y0));
    }
    public static System.Drawing.Rectangle Margin(System.Drawing.Rectangle wa)
    {
        int m = (int)(Math.Min(wa.Width, wa.Height) * Math.Clamp(Settings.Current.LayoutMarginPct, 0, 25) / 100.0);
        return new System.Drawing.Rectangle(wa.Left + m, wa.Top + m, wa.Width - 2 * m, wa.Height - 2 * m);
    }
}

// What the switcher and the gestures need to know about the windows on the desktop.
public class WinInfo
{
    public IntPtr Handle; public string Title = "", Exe = "", Path = ""; public uint Pid; public bool Minimized;
}
public static class WinList
{
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr h, bool alt);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr res);
    [DllImport("user32.dll")] static extern IntPtr GetClassLongPtr(IntPtr h, int i);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder sb, ref int size);

    static readonly Dictionary<uint, string> _paths = new();
    public static string PathOf(uint pid)
    {
        if (_paths.TryGetValue(pid, out var c)) return c;
        string p = "";
        var h = OpenProcess(0x1000, false, pid);
        if (h != IntPtr.Zero) { var sb = new StringBuilder(520); int n = sb.Capacity; if (QueryFullProcessImageName(h, 0, sb, ref n)) p = sb.ToString(); CloseHandle(h); }
        if (_paths.Count > 300) _paths.Clear();
        return _paths[pid] = p;
    }
    public static string Class(IntPtr h) { var sb = new StringBuilder(80); GetClassName(h, sb, 80); return sb.ToString(); }
    public static bool IsPlain(IntPtr h)
    {
        var c = Class(h);
        return !(c is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow" or "NotifyIconOverflowWindow");
    }
    // Windows you can switch to, most recently used first.
    public static List<WinInfo> List(bool includeMinimized = true, string[]? hiddenExes = null)
    {
        var res = new List<WinInfo>(); uint self = (uint)Environment.ProcessId;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            long ex = GetWindowLongPtr(h, -20).ToInt64();
            if ((ex & 0x80) != 0 && (ex & 0x40000) == 0) return true;             // a tool window
            if (GetWindow(h, 4) != IntPtr.Zero && (ex & 0x40000) == 0) return true; // owned by another window
            if (DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0) return true; // on another desktop or suspended
            bool min = IsIconic(h); if (min && !includeMinimized) return true;
            var sb = new StringBuilder(260); GetWindowText(h, sb, 260); if (sb.Length == 0) return true;
            GetWindowThreadProcessId(h, out uint pid); if (pid == self) return true;
            if (!IsPlain(h)) return true;
            var path = PathOf(pid); var exe = System.IO.Path.GetFileName(path);
            if (hiddenExes != null && hiddenExes.Any(x => x.Equals(exe, StringComparison.OrdinalIgnoreCase))) return true;
            res.Add(new WinInfo { Handle = h, Title = sb.ToString(), Exe = exe, Path = path, Pid = pid, Minimized = min });
            return true;
        }, IntPtr.Zero);
        return res;
    }
    public static void Activate(IntPtr h)
    {
        if (IsIconic(h)) ShowWindowAsync(h, 9);
        SwitchToThisWindow(h, true); SetForegroundWindow(h);
    }
    public static void Close(IntPtr h) => PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero);
    static readonly Dictionary<string, ImageSource?> _icons = new();
    // The window's own icon, or its program's.
    public static ImageSource? Icon(WinInfo w)
    {
        string key = w.Path.Length > 0 ? w.Path : "h" + w.Handle;
        if (_icons.TryGetValue(key, out var c)) return c;
        ImageSource? src = null;
        try
        {
            IntPtr hicon = IntPtr.Zero;
            if (SendMessageTimeout(w.Handle, 0x007F, (IntPtr)1, IntPtr.Zero, 2, 120, out var r) != IntPtr.Zero) hicon = r;
            if (hicon == IntPtr.Zero) hicon = GetClassLongPtr(w.Handle, -14);
            bool frame = w.Exe.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
            if (w.Path.Length > 0 && !frame && System.IO.File.Exists(w.Path))
            {
                using var ic = System.Drawing.Icon.ExtractAssociatedIcon(w.Path);
                if (ic != null) src = Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            if (src == null && hicon != IntPtr.Zero) src = Imaging.CreateBitmapSourceFromHIcon(hicon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src?.Freeze();
        }
        catch { }
        if (_icons.Count > 200) _icons.Clear();
        return _icons[key] = src;
    }
}

// ───────────────────────── Window switcher ─────────────────────────
// Takes over Alt+Tab: one card per window, with a live preview, a title and a search by typing.
public static class Switcher
{
    public static volatile bool Open, HoldMode, Searching;
    static SwitcherWindow? _win; static List<WinInfo> _items = new(); static int _sel; static string _query = "";
    static DispatcherTimer? _delay; static bool _sameApp;
    static IntPtr _origin;

    public static bool Enabled => Settings.Current.SwitcherOn;

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetCursorPos(out System.Drawing.Point p);

    // Called on the UI thread when Alt+Tab (or the hotkey) is pressed, and again for each further Tab.
    public static void Step(int dir, bool hold, bool sameApp = false)
    {
        var s = Settings.Current;
        if (!Open)
        {
            var hidden = s.SwitcherHiddenApps.ToArray();
            var list = WinList.List(s.SwitcherMinimized, hidden);
            _origin = GetForegroundWindow();
            if (sameApp)
            {
                GetWindowThreadProcessIdW(_origin, out uint fp); var fpath = WinList.PathOf(fp);
                list = list.Where(w => w.Path.Equals(fpath, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            if (s.SwitcherCurrentDisplay)
            {
                GetCursorPos(out var pt); var scr = System.Windows.Forms.Screen.FromPoint(pt);
                list = list.Where(w => System.Windows.Forms.Screen.FromHandle(w.Handle).DeviceName == scr.DeviceName).ToList();
            }
            if (list.Count == 0) return;
            _items = list; _sameApp = sameApp; _query = ""; Searching = false; HoldMode = hold;
            // the window in front is first in the list; Tab goes to the one behind it
            int at = list.FindIndex(w => w.Handle == _origin);
            _sel = list.Count == 1 ? 0 : dir > 0 ? (at == 0 || at < 0 ? 1 : 0) : list.Count - 1;
            Open = true;
            int delay = hold ? Math.Clamp(s.SwitcherDelayMs, 0, 600) : 0;
            if (delay > 0)
            {
                _delay?.Stop();
                _delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
                _delay.Tick += (_, _) => { _delay!.Stop(); if (Open) Show(); };
                _delay.Start();
            }
            else Show();
            return;
        }
        var vis = Visible();
        if (vis.Count == 0) return;
        int cur = Math.Max(0, vis.FindIndex(w => w.Handle == _items[Math.Clamp(_sel, 0, _items.Count - 1)].Handle));
        cur = (cur + dir + vis.Count) % vis.Count;
        _sel = _items.FindIndex(w => w.Handle == vis[cur].Handle);
        if (_delay is { IsEnabled: true }) { _delay.Stop(); Show(); }
        _win?.Select(_sel);
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] static extern uint GetWindowThreadProcessIdW(IntPtr h, out uint pid);

    static List<WinInfo> Visible() => _query.Length == 0 ? _items : _items.Where(w => (w.Title + " " + w.Exe).Contains(_query, StringComparison.OrdinalIgnoreCase)).ToList();
    static void Show()
    {
        _win?.Close();
        _win = new SwitcherWindow(_items, () => _sel, i => { _sel = i; Commit(); }, _query);
        _win.Show(); _win.Select(_sel);
    }
    public static void Toggle() { if (Open) Cancel(); else Step(1, false); }
    public static void Type(char c)
    {
        if (!Open) return;
        Searching = true; _query += c;
        var vis = Visible(); if (vis.Count > 0) _sel = _items.FindIndex(w => w.Handle == vis[0].Handle);
        if (_delay is { IsEnabled: true }) { _delay.Stop(); Show(); }
        _win?.SetQuery(_query, vis, _items, _sel);
    }
    public static void Backspace()
    {
        if (!Open || _query.Length == 0) return;
        _query = _query[..^1]; if (_query.Length == 0) Searching = false;
        var vis = Visible(); if (vis.Count > 0) _sel = _items.FindIndex(w => w.Handle == vis[0].Handle);
        _win?.SetQuery(_query, vis, _items, _sel);
    }
    public static void Move(int dx, int dy)
    {
        if (!Open || _win == null) return;
        var vis = Visible(); if (vis.Count == 0) return;
        int cols = _win.Columns, cur = Math.Max(0, vis.FindIndex(w => w.Handle == _items[Math.Clamp(_sel, 0, _items.Count - 1)].Handle));
        int next = Math.Clamp(cur + dx + dy * cols, 0, vis.Count - 1);
        _sel = _items.FindIndex(w => w.Handle == vis[next].Handle); _win.Select(_sel);
    }
    public static void Commit()
    {
        if (!Open) return;
        var vis = Visible(); var chosen = _sel >= 0 && _sel < _items.Count ? _items[_sel] : null;
        if (chosen == null || !vis.Contains(chosen)) chosen = vis.FirstOrDefault();
        Close();
        if (chosen != null) WinList.Activate(chosen.Handle);
    }
    public static void CloseSelected()
    {
        if (!Open || _sel < 0 || _sel >= _items.Count) return;
        var w = _items[_sel]; WinList.Close(w.Handle);
        _items.Remove(w); if (_items.Count == 0) { Close(); return; }
        _sel = Math.Clamp(_sel, 0, _items.Count - 1); Show();
    }
    public static void Cancel() => Close();
    static void Close()
    {
        Open = false; Searching = false; _delay?.Stop(); _delay = null;
        var w = _win; _win = null; w?.Close();
    }
}

public class SwitcherWindow : Window
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int W, H; }
    [StructLayout(LayoutKind.Sequential)] struct THUMB { public uint Flags; public RECT Dest, Src; public byte Opacity; public bool Visible, ClientOnly; }
    [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int L, R, T, B; }
    [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
    [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumb);
    [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref THUMB p);
    [DllImport("dwmapi.dll")] static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE s);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

    readonly List<(WinInfo W, Border Card, Border Preview, IntPtr Thumb)> _cards = new();
    readonly WrapPanel _wrap = new(); readonly TextBlock _search = T("", 13, "#FFFFFF"), _count = T("", 11, Dim);
    readonly Func<int> _selFn; readonly Action<int> _pick; string _layout;
    List<WinInfo> _all; int _selected; bool _mouseMoved; Border _searchBox;
    public int Columns { get; private set; } = 4;

    public SwitcherWindow(List<WinInfo> all, Func<int> sel, Action<int> pick, string query)
    {
        _all = all; _selFn = sel; _pick = pick; _layout = Settings.Current.SwitcherLayout;
        WindowStyle = WindowStyle.None; AllowsTransparency = false; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false;
        Topmost = true; Background = Brushes.Transparent; UseLayoutRounding = true; Title = "Perch switcher";
        _wrap.HorizontalAlignment = HorizontalAlignment.Center; _wrap.Margin = new Thickness(4, 8, 4, 8);
        _searchBox = new Border
        {
            CornerRadius = new CornerRadius(10), Background = B("#26FFFFFF"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 220, Child = Row(Ico("", 12, Dim).M(0, 0, 8), _search)
        };
        var root = new Border { Background = B("#7A121214"), Padding = new Thickness(16, 14, 16, 12), Child = Col(_searchBox, _wrap, _count.With(c => { c.HorizontalAlignment = HorizontalAlignment.Center; c.Margin = new Thickness(0, 2, 0, 0); })) };
        Content = root;
        Build(all, query);
        SourceInitialized += (_, _) => Backdrop();
        ContentRendered += (_, _) => Thumbs();
        PreviewMouseMove += (_, _) => _mouseMoved = true;
        Closed += (_, _) => Drop();
    }
    void Backdrop()
    {
        var h = new WindowInteropHelper(this).Handle;
        var src = HwndSource.FromHwnd(h); if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
        var m = new MARGINS { L = -1, R = -1, T = -1, B = -1 }; DwmExtendFrameIntoClientArea(h, ref m);
        int one = 1, round = 2, acrylic = 3;
        DwmSetWindowAttribute(h, 20, ref one, 4); DwmSetWindowAttribute(h, 33, ref round, 4); DwmSetWindowAttribute(h, 38, ref acrylic, 4);
        SetWindowLong(h, -20, GetWindow(h) | 0x80 | 0x08000000); // never takes focus, never in Alt+Tab itself
    }
    static int GetWindow(IntPtr h) => GetWindowLong(h, -20);

    void Build(List<WinInfo> all, string query)
    {
        Drop(); _cards.Clear(); _wrap.Children.Clear();
        var s = Settings.Current; var vis = query.Length == 0 ? all : all.Where(w => (w.Title + " " + w.Exe).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        var wa = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        double cw = _layout == "list" ? 560 : _layout == "icons" ? 118 : 226, ch = _layout == "list" ? 40 : _layout == "icons" ? 118 : 168;
        int cols = _layout == "list" ? 1 : Math.Clamp(s.SwitcherColumns, 2, 8);
        cols = Math.Max(1, Math.Min(cols, (int)((wa.Width * 0.9 - 40) / (cw + 8))));
        Columns = Math.Min(cols, Math.Max(1, vis.Count));
        int rows = (int)Math.Ceiling(vis.Count / (double)Columns);
        double maxRows = Math.Max(1, Math.Floor((wa.Height * 0.8 - 80) / (ch + 8)));
        _wrap.Width = Columns * (cw + 8);
        _searchBox.Visibility = query.Length > 0 ? Visibility.Visible : Visibility.Collapsed; _search.Text = query;
        foreach (var w in vis)
        {
            var win = w;
            var icon = new Image { Source = WinList.Icon(win), Width = _layout == "list" ? 20 : 18, Height = _layout == "list" ? 20 : 18, VerticalAlignment = VerticalAlignment.Center };
            var title = T(win.Title, 11.5, "#FFFFFF", true); title.TextTrimming = TextTrimming.CharacterEllipsis; title.VerticalAlignment = VerticalAlignment.Center;
            var preview = new Border { CornerRadius = new CornerRadius(7), Background = B("#18FFFFFF") };
            UIElement body;
            if (_layout == "previews")
            {
                var g = MakeGrid("*", "auto,*"); var head = MakeGrid("auto,*", "auto"); head.Children.Add(icon.At(0, 0)); head.Children.Add(title.M(7, 0, 0, 0).At(0, 1));
                g.Children.Add(head.At(0, 0)); g.Children.Add(preview.M(0, 7, 0, 0).At(1, 0));
                if (win.Minimized) preview.Child = Col(new Image { Source = icon.Source, Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 4) }, T("Minimised", 10, Dim).With(x => x.HorizontalAlignment = HorizontalAlignment.Center));
                body = g;
            }
            else if (_layout == "icons")
            {
                icon.Width = icon.Height = 52; icon.HorizontalAlignment = HorizontalAlignment.Center;
                title.TextAlignment = TextAlignment.Center; title.FontSize = 10.5;
                body = Col(icon.M(0, 6, 0, 8), title); preview = new Border();
            }
            else
            {
                var g = MakeGrid("auto,*,auto", "auto"); g.Children.Add(icon.At(0, 0)); g.Children.Add(title.M(10, 0, 8, 0).At(0, 1)); g.Children.Add(T(win.Exe, 10.5, Dim).At(0, 2)); body = g; preview = new Border();
            }
            var card = new Border { Width = cw, Height = ch, Margin = new Thickness(4), CornerRadius = new CornerRadius(12), Background = B("#14FFFFFF"), BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(2), Padding = new Thickness(_layout == "list" ? 12 : 9), Child = body, Cursor = Cursors.Hand };
            int idx = all.IndexOf(win);
            card.MouseEnter += (_, _) => { if (_mouseMoved) Highlight(win); };
            card.MouseLeftButtonUp += (_, _) => _pick(idx);
            card.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { Switcher.CloseSelected(); } };
            _wrap.Children.Add(card); _cards.Add((win, card, preview, IntPtr.Zero));
        }
        _count.Text = vis.Count == 0 ? "No window matches" : (query.Length > 0 ? $"{vis.Count} of {all.Count} windows" : "Tab or arrows to choose · Enter or release Alt to switch · Esc to cancel · type to search");
        Width = Math.Max(300, _wrap.Width + 40); Height = Math.Min(wa.Height * 0.85, rows * (ch + 8) + 96 + (query.Length > 0 ? 44 : 0));
        Left = wa.Left + (wa.Width - Width) / 2; Top = wa.Top + (wa.Height - Height) / 2;
    }
    void Highlight(WinInfo w)
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            bool on = _cards[i].W == w;
            _cards[i].Card.BorderBrush = on ? B("#FFFFFFFF") : Brushes.Transparent; _cards[i].Card.Background = on ? B("#33FFFFFF") : B("#14FFFFFF");
        }
        _selected = _all.IndexOf(w);
    }
    public void Select(int index)
    {
        if (index < 0 || index >= _all.Count) return;
        Highlight(_all[index]); _mouseMoved = false;
    }
    public void SetQuery(string query, List<WinInfo> vis, List<WinInfo> all, int sel)
    {
        _all = all; Build(all, query); UpdateLayout(); Thumbs(); if (sel >= 0 && sel < all.Count) Highlight(all[sel]);
    }
    void Thumbs()
    {
        if (_layout != "previews") return;
        var h = new WindowInteropHelper(this).Handle; if (h == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        for (int i = 0; i < _cards.Count; i++)
        {
            var (w, card, prev, _) = _cards[i];
            if (w.Minimized || prev.ActualWidth < 4) continue;
            if (DwmRegisterThumbnail(h, w.Handle, out var th) != 0) continue;
            DwmQueryThumbnailSourceSize(th, out var sz); if (sz.W <= 0 || sz.H <= 0) { DwmUnregisterThumbnail(th); continue; }
            var tl = prev.TransformToAncestor((Visual)Content).Transform(new Point(0, 0));
            double bw = prev.ActualWidth * dpi.DpiScaleX, bh = prev.ActualHeight * dpi.DpiScaleY, k = Math.Min(bw / sz.W, bh / sz.H);
            if (k > 1) k = 1;
            int tw = (int)(sz.W * k), thh = (int)(sz.H * k);
            int x = (int)(tl.X * dpi.DpiScaleX + (bw - tw) / 2), y = (int)(tl.Y * dpi.DpiScaleY + (bh - thh) / 2);
            var p = new THUMB { Flags = 1 | 4 | 8, Dest = new RECT { L = x, T = y, R = x + tw, B = y + thh }, Opacity = 255, Visible = true };
            DwmUpdateThumbnailProperties(th, ref p);
            _cards[i] = (w, card, prev, th);
        }
    }
    void Drop()
    {
        for (int i = 0; i < _cards.Count; i++) if (_cards[i].Thumb != IntPtr.Zero) { DwmUnregisterThumbnail(_cards[i].Thumb); _cards[i] = (_cards[i].W, _cards[i].Card, _cards[i].Preview, IntPtr.Zero); }
    }
}

// ───────────────────────── hook side: switcher keys, drag gestures, desktop gestures ─────────────────────────
public static partial class Hooks
{
    static volatile bool _extMouse, _extKeys, _gestureBusy;
    static volatile bool _swOn, _swSame, _gOn, _gSnap, _gRaise, _deskOn, _musicOn;
    static int _gKey; static int _deskBtn = 1; static bool _deskFollow = true;
    static HashSet<string> _layoutIgnored = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out WRECT r);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] struct WRECT { public int L, T, R, B; }

    static void RefreshExt(Settings s)
    {
        _swOn = s.SwitcherOn; _swSame = s.SwitcherSameApp;
        _gOn = s.GestureOn; _gSnap = s.GestureEdgeSnap; _gRaise = s.GestureRaise;
        _gKey = s.GestureKey switch { "ctrlalt" => 3, "win" => 4, _ => 1 };
        _deskOn = s.DeskGesture; _deskBtn = s.DeskGestureButton switch { "x1" => 4, "x2" => 5, _ => 3 }; _deskFollow = s.DeskFollow;
        _layoutIgnored = new HashSet<string>(s.LayoutIgnoredApps, StringComparer.OrdinalIgnoreCase);
        _musicOn = s.MusicBlock; _extMouse = _gOn || _deskOn; _extKeys = _swOn || _musicOn;
        if (!_gOn && !_deskOn) _gestureBusy = false;
    }

    static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    static bool GestureKeysDown() => _gKey switch { 3 => Down(0x11) && Down(0x12), 4 => Down(0x5B) || Down(0x5C), _ => Down(0x12) && !Down(0x11) };

    // ── the switcher's keys ──
    static bool KeyboardExt(int msg, KBD k)
    {
        bool up = (k.flags & 0x80) != 0; int vk = (int)k.vk; bool alt = (k.flags & 0x20) != 0;
        if (_musicOn && !up && vk == 0xB3) MediaAt = Environment.TickCount64;
        if (!_swOn) return false;
        if (!Switcher.Open)
        {
            if (up) return false;
            if (vk == 0x09 && alt && !Down(0x11)) { bool shift = Down(0x10); Svc.Ui(() => Switcher.Step(shift ? -1 : 1, true)); NoMenu(); _kSwallowed[vk] = true; return true; }
            if (vk == 0xC0 && alt && _swSame && !Down(0x11)) { bool shift = Down(0x10); Svc.Ui(() => Switcher.Step(shift ? -1 : 1, true, true)); NoMenu(); _kSwallowed[vk] = true; return true; }
            return false;
        }
        // open: the list owns the keyboard
        if (vk is 0x12 or 0xA4 or 0xA5)
        {
            if (up && Switcher.HoldMode && !Switcher.Searching) Svc.Ui(Switcher.Commit);
            return false;
        }
        if (vk is 0x10 or 0xA0 or 0xA1 or 0x11 or 0xA2 or 0xA3) return false;
        if (up) { if (_kSwallowed[vk]) { _kSwallowed[vk] = false; return true; } return false; }
        _kSwallowed[vk] = true;
        bool sh = Down(0x10);
        switch (vk)
        {
            case 0x09: case 0xC0: Svc.Ui(() => Switcher.Step(sh ? -1 : 1, Switcher.HoldMode)); break;
            case 0x1B: Svc.Ui(Switcher.Cancel); break;
            case 0x0D: Svc.Ui(Switcher.Commit); break;
            case 0x25: Svc.Ui(() => Switcher.Move(-1, 0)); break;
            case 0x27: Svc.Ui(() => Switcher.Move(1, 0)); break;
            case 0x26: Svc.Ui(() => Switcher.Move(0, -1)); break;
            case 0x28: Svc.Ui(() => Switcher.Move(0, 1)); break;
            case 0x2E: Svc.Ui(Switcher.CloseSelected); break;
            case 0x08: Svc.Ui(Switcher.Backspace); break;
            case 0x20: Svc.Ui(() => Switcher.Type(' ')); break;
            default:
                if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) { char c = (char)(vk >= 0x41 ? vk + 32 : vk); Svc.Ui(() => Switcher.Type(c)); }
                break;
        }
        return true;
    }
    // An unused key between Alt going down and up, so the program in front never opens its menu bar.
    static void NoMenu() => Send(new List<INPUT> { Key(0xE8, false), Key(0xE8, true) });

    // ── drag gestures ──
    static IntPtr _gw; static int _gx0, _gy0; static WRECT _gr0; static bool _gResize, _gDragging, _gArmed; static int _gBtn; static long _gLast;
    static int _gZoneX, _gZoneY; static string _gSnapMode = ""; static int _dx0, _dy0; static bool _deskDown, _deskMoved;

    static bool MouseExt(int msg, MSL m)
    {
        // desktop gesture: a held button plus a drag switches virtual desktops
        if (_deskOn)
        {
            int b = msg switch { 0x0207 or 0x0208 => 3, 0x020B or 0x020C => ((m.data >> 16) & 0xFFFF) == 1 ? 4 : 5, _ => 0 };
            bool isDown = msg is 0x0207 or 0x020B;
            if (b == _deskBtn && !IgnoredAt(m.x, m.y))
            {
                if (isDown) { _deskDown = true; _deskMoved = false; _dx0 = m.x; _dy0 = m.y; _gestureBusy = true; return true; }
                if (_deskDown)
                {
                    _deskDown = false; _gestureBusy = _gArmed || _gDragging;
                    int dx = m.x - _dx0, dy = m.y - _dy0; const int TH = 70;
                    if (Math.Abs(dx) >= TH && Math.Abs(dx) >= Math.Abs(dy))
                    {
                        bool next = _deskFollow ? dx < 0 : dx > 0;
                        Send(new List<INPUT> { Key(0x5B, false), Key(0x11, false), Key(next ? (ushort)0x27 : (ushort)0x25, false), Key(next ? (ushort)0x27 : (ushort)0x25, true), Key(0x11, true), Key(0x5B, true) });
                    }
                    else if (dy <= -TH) Press(0x5B, 0x09);       // up: all windows
                    else if (dy >= TH) Press(0x5B, 0x44);        // down: show the desktop
                    else ReplayButton(b);
                    return true;
                }
            }
            if (msg == 0x0200 && _deskDown) { _deskMoved = true; return false; }
        }
        if (!_gOn) return false;
        switch (msg)
        {
            case 0x0201: case 0x0204: // left or right button down
                {
                    bool resizeRight = msg == 0x0204;
                    if (!GestureKeysDown()) return false;
                    var h = GetAncestor(WindowFromPoint(new POINT { X = m.x, Y = m.y }), 2);
                    if (h == IntPtr.Zero || !WinList.IsPlain(h)) return false;
                    GetWindowThreadProcessId(h, out uint pid); if (pid == (uint)Environment.ProcessId) return false;
                    if (_layoutIgnored.Count > 0 && _layoutIgnored.Contains(System.IO.Path.GetFileName(WinList.PathOf(pid)))) return false;
                    if (!GetWindowRect(h, out _gr0)) return false;
                    _gw = h; _gx0 = m.x; _gy0 = m.y; _gBtn = msg; _gDragging = true; _gArmed = true; _gestureBusy = true;
                    _gResize = resizeRight || Down(0x10);
                    if (IsZoomed(h)) { ShowWindowAsync(h, 9); _gr0 = new WRECT { L = m.x - 400, T = m.y - 14, R = m.x + 400, B = m.y + 500 }; }
                    int w = _gr0.R - _gr0.L, hgt = _gr0.B - _gr0.T;
                    int fx = (m.x - _gr0.L) * 3 / Math.Max(1, w), fy = (m.y - _gr0.T) * 3 / Math.Max(1, hgt);
                    _gZoneX = fx < 1 ? -1 : fx >= 2 ? 1 : 0; _gZoneY = fy < 1 ? -1 : fy >= 2 ? 1 : 0;
                    if (_gZoneX == 0 && _gZoneY == 0) { _gZoneX = 1; _gZoneY = 1; }
                    if (_gRaise) SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x4000);
                    NoMenu();
                    return true;
                }
            case 0x0200: // movement
                {
                    if (!_gDragging) return false;
                    long now = Environment.TickCount64; if (now - _gLast < 7) return false; _gLast = now;
                    int dx = m.x - _gx0, dy = m.y - _gy0; int l = _gr0.L, t = _gr0.T, r = _gr0.R, b = _gr0.B;
                    if (!_gResize) { l += dx; r += dx; t += dy; b += dy; }
                    else
                    {
                        if (_gZoneX < 0) l += dx; else if (_gZoneX > 0) r += dx;
                        if (_gZoneY < 0) t += dy; else if (_gZoneY > 0) b += dy;
                        if (r - l < 160) { if (_gZoneX < 0) l = r - 160; else r = l + 160; }
                        if (b - t < 100) { if (_gZoneY < 0) t = b - 100; else b = t + 100; }
                    }
                    SetWindowPos(_gw, IntPtr.Zero, l, t, r - l, b - t, 0x0004 | 0x0010 | 0x4000);
                    _gSnapMode = _gSnap && !_gResize ? EdgeZone(m.x, m.y) : "";
                    return false;
                }
            case 0x0202: case 0x0205: // button up
                {
                    if (!_gDragging || (msg == 0x0202) != (_gBtn == 0x0201)) return false;
                    _gDragging = false; _gArmed = false; _gestureBusy = _deskDown;
                    var h = _gw; var mode = _gSnap && !_gResize ? EdgeZone(m.x, m.y) : ""; _gSnapMode = "";
                    if (mode.Length > 0) Svc.Ui(() => Snap.To(mode, h));
                    return true;
                }
        }
        return false;
    }
    // Which snap zone the pointer is in, when it is at the edge of its display.
    static string EdgeZone(int x, int y)
    {
        var scr = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x, y)).Bounds; const int E = 6;
        bool l = x <= scr.Left + E, r = x >= scr.Right - E - 1, t = y <= scr.Top + E, bt = y >= scr.Bottom - E - 1;
        if (t && l) return "tl"; if (t && r) return "tr"; if (bt && l) return "bl"; if (bt && r) return "br";
        if (l) return "left"; if (r) return "right"; if (t) return "max";
        return "";
    }
    static void ReplayButton(int b)
    {
        var inputs = new List<INPUT>();
        uint down = b == 3 ? 0x0020u : 0x0080u, upf = b == 3 ? 0x0040u : 0x0100u; int data = b == 4 ? 1 : b == 5 ? 2 : 0;
        inputs.Add(new INPUT { type = 0, u = new INPUTUNION { mi = new MOUSEINPUT { mouseData = data, dwFlags = down, dwExtraInfo = Magic } } });
        inputs.Add(new INPUT { type = 0, u = new INPUTUNION { mi = new MOUSEINPUT { mouseData = data, dwFlags = upf, dwExtraInfo = Magic } } });
        Send(inputs);
    }
}

public static class Windows2
{
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    // Puts the pointer in the middle of the next display.
    public static void PointerToNextDisplay()
    {
        var all = System.Windows.Forms.Screen.AllScreens; if (all.Length < 2) return;
        var cur = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        var to = all[(Array.FindIndex(all, x => x.DeviceName == cur.DeviceName) + 1) % all.Length].Bounds;
        SetCursorPos(to.Left + to.Width / 2, to.Top + to.Height / 2);
    }
}
