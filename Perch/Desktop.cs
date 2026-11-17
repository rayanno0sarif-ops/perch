using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static Perch.UI;

namespace Perch;

public partial class Settings
{
    public bool GlassTaskbar { get; set; }                 // Perch's bar replaces the Windows taskbar
    public int TaskbarPrevState { get; set; } = -1;        // the Windows taskbar's auto-hide state before Perch took over; -1 is "not taken over"
    public string LiveWallpaper { get; set; } = "";        // a video or picture drawn behind the desktop icons
    public bool LiveWallpaperOn { get; set; }
    public List<string> DeskWidgets { get; set; } = new(); // weather | system | music
    public Dictionary<string, double[]> DeskWidgetPos { get; set; } = new();
}

static class Shell
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr res);
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
    [StructLayout(LayoutKind.Sequential)] public struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage, uEdge; public RECT rc; public IntPtr lParam; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("shell32.dll")] public static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA d);
    public static void NoFocus(Window w) { var h = new WindowInteropHelper(w).Handle; SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80 | 0x08000000); }
    public static void ToBottom(Window w) { var h = new WindowInteropHelper(w).Handle; if (h != IntPtr.Zero) SetWindowPos(h, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }
}

// ───────────────────────── Glass taskbar ─────────────────────────
// Hides the Windows taskbar and shows a floating glass bar: Start, your launcher slots, open programs, and a status corner.
// The Windows taskbar is always put back when this is turned off or Perch closes, and on the next start if Perch ever crashed.
public class GlassTaskbar : Window
{
    static GlassTaskbar? _bar; static DispatcherTimer? _keep;
    static Settings S => Settings.Current;

    public static void Sync()
    {
        if (S.GlassTaskbar)
        {
            HideWindows(true);
            if (_bar == null) { _bar = new GlassTaskbar(); _bar.Show(); }
            _keep ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _keep.Tick -= Keep; _keep.Tick += Keep; _keep.Start();
        }
        else { _keep?.Stop(); _bar?.Close(); _bar = null; HideWindows(false); }
    }
    public static void Restore() { if (S.TaskbarPrevState >= 0) HideWindows(false); }
    static void Keep(object? s, EventArgs e) { HideWindows(true); _bar?.Refresh(); } // Explorer restarts bring its taskbar back

    static void HideWindows(bool hide)
    {
        var tray = Shell.FindWindow("Shell_TrayWnd", null);
        var d = new Shell.APPBARDATA { cbSize = Marshal.SizeOf<Shell.APPBARDATA>(), hWnd = tray };
        if (hide)
        {
            if (S.TaskbarPrevState < 0) { S.TaskbarPrevState = (int)Shell.SHAppBarMessage(4, ref d); S.Save(); } // ABM_GETSTATE
            d.lParam = (IntPtr)1; Shell.SHAppBarMessage(10, ref d); // ABM_SETSTATE: auto-hide frees the space at the bottom
        }
        else if (S.TaskbarPrevState >= 0) { d.lParam = (IntPtr)S.TaskbarPrevState; Shell.SHAppBarMessage(10, ref d); S.TaskbarPrevState = -1; S.Save(); }
        Shell.ShowWindow(tray, hide ? 0 : 5);
        IntPtr sec = IntPtr.Zero;
        while ((sec = Shell.FindWindowEx(IntPtr.Zero, sec, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) Shell.ShowWindow(sec, hide ? 0 : 5);
    }

    readonly StackPanel _apps = new() { Orientation = Orientation.Horizontal };
    readonly TextBlock _time = T("", 11.5, "#FFFFFF", true), _date = T("", 10, Dim);
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    string _sig = "";

    GlassTaskbar()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; ShowActivated = false; Title = "Perch taskbar";
        SizeToContent = SizeToContent.Width; Height = 64;
        var start = Btn(Ico("", 16), () => Hooks.Press(0x5B), "#00FFFFFF", 12, new Thickness(10, 8, 10, 8)); start.ToolTip = "Start";
        var search = Btn(Ico("", 14), () => Hooks.Press(0x5B, 0x53), "#00FFFFFF", 12, new Thickness(10, 8, 10, 8)); search.ToolTip = "Search";
        var status = Btn(Row(Ico("", 12), Ico("", 12).M(8), Ico("", 12).M(8)), () => Hooks.Press(0x5B, 0x41), "#00FFFFFF", 12, new Thickness(10, 8, 10, 8)); status.ToolTip = "Quick settings";
        var clock = Btn(Col(_time.With(t => t.HorizontalAlignment = HorizontalAlignment.Right), _date.With(t => t.HorizontalAlignment = HorizontalAlignment.Right)), () => Hooks.Press(0x5B, 0x4E), "#00FFFFFF", 12, new Thickness(10, 4, 12, 4)); clock.ToolTip = "Notifications and calendar";
        var sep = new Border { Width = 1, Height = 22, Background = B("#33FFFFFF"), Margin = new Thickness(6, 0, 6, 0) };
        var sep2 = new Border { Width = 1, Height = 22, Background = B("#33FFFFFF"), Margin = new Thickness(6, 0, 6, 0) };
        var row = Row(start, search, sep, _apps, sep2, status, clock);
        Content = new Border
        {
            Margin = new Thickness(10, 6, 10, 10), CornerRadius = new CornerRadius(18), Padding = new Thickness(6, 0, 6, 0), Background = Glass.Surface(false), BorderBrush = Glass.On ? Glass.Rim : Glass.RimSolid, BorderThickness = new Thickness(1),
            Child = row, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.45 }
        };
        SizeChanged += (_, _) => Place();
        SourceInitialized += (_, _) => Shell.NoFocus(this);
        _tick.Tick += (_, _) => Clock(); _tick.Start(); Closed += (_, _) => _tick.Stop();
        Clock(); Refresh(); Place();
    }
    void Place() { var wa = SystemParameters.PrimaryScreenWidth; Left = (wa - ActualWidth) / 2; Top = SystemParameters.PrimaryScreenHeight - Height; }
    void Clock() { var n = DateTime.Now; _time.Text = UI.TimeText(n); _date.Text = n.ToString("d"); }

    // Launcher slots first, then programs with open windows that are not already a slot.
    public void Refresh()
    {
        var running = WinList.List(true).GroupBy(w => w.Path, StringComparer.OrdinalIgnoreCase).Where(g => g.Key.Length > 0).ToList();
        var slots = S.Slots.Where(p => p.Length > 0 && File.Exists(p)).ToList();
        string sig = string.Join("|", slots) + "#" + string.Join("|", running.Select(g => g.Key + g.Count() + g.Any(w => w.Handle == Fg())));
        if (sig == _sig) return; _sig = sig; _apps.Children.Clear();
        foreach (var p in slots) _apps.Children.Add(AppButton(p, running.FirstOrDefault(g => g.Key.Equals(p, StringComparison.OrdinalIgnoreCase))?.ToList()));
        foreach (var g in running.Where(g => !slots.Contains(g.Key, StringComparer.OrdinalIgnoreCase))) _apps.Children.Add(AppButton(g.Key, g.ToList()));
    }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    static IntPtr Fg() => GetForegroundWindow();
    Border AppButton(string path, List<WinInfo>? wins)
    {
        bool open = wins is { Count: > 0 }, front = open && wins!.Any(w => w.Handle == Fg());
        var icon = new Image { Source = Native.FileIcon(path), Width = 26, Height = 26 };
        var dot = new Border { Width = front ? 14 : 5, Height = 3, CornerRadius = new CornerRadius(1.5), Background = B(front ? "#FFFFFF" : "#99FFFFFF"), Opacity = open ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
        var b = Btn(Col(icon, dot), () =>
        {
            if (!open) { Svc.Open(path); return; }
            var w = wins![0];
            if (front && !IsIconic(w.Handle)) Shell.ShowWindow(w.Handle, 6); else WinList.Activate(w.Handle); // clicking the program in front minimises it, like Windows
            _sig = "";
        }, front ? "#26FFFFFF" : "#00FFFFFF", 10, new Thickness(6, 7, 6, 3));
        b.ToolTip = open ? wins![0].Title : Path.GetFileNameWithoutExtension(path); b.Margin = new Thickness(1, 0, 1, 0);
        if (open) b.MouseRightButtonUp += (_, e) => { foreach (var w in wins!) WinList.Close(w.Handle); e.Handled = true; _sig = ""; };
        return b;
    }
}

// ───────────────────────── Live wallpaper ─────────────────────────
// A video (looping, muted) or picture drawn behind the desktop icons, in the same layer Windows draws its own wallpaper.
public class LiveWallpaper : Window
{
    static LiveWallpaper? _w;
    public static void Sync()
    {
        var s = Settings.Current; bool want = s.LiveWallpaperOn && File.Exists(s.LiveWallpaper);
        if (!want) { _w?.Close(); _w = null; return; }
        _w?.Close(); _w = new LiveWallpaper(s.LiveWallpaper); _w.Show();
    }
    static readonly string[] Video = { ".mp4", ".wmv", ".mov", ".avi", ".mkv", ".webm", ".m4v" };
    LiveWallpaper(string file)
    {
        WindowStyle = WindowStyle.None; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; ShowActivated = false; Background = Brushes.Black; Title = "Perch wallpaper";
        var b = System.Windows.Forms.Screen.PrimaryScreen!.Bounds; Left = 0; Top = 0; Width = b.Width; Height = b.Height;
        if (Video.Contains(Path.GetExtension(file).ToLowerInvariant()))
        {
            var m = new MediaElement { Source = new Uri(file), IsMuted = true, Stretch = Stretch.UniformToFill, LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual };
            m.MediaEnded += (_, _) => { m.Position = TimeSpan.Zero; m.Play(); };
            Loaded += (_, _) => m.Play(); Content = m;
        }
        else Content = new Image { Source = new BitmapImage(new Uri(file)), Stretch = Stretch.UniformToFill };
        SourceInitialized += (_, _) => Attach(new WindowInteropHelper(this).Handle, b);
    }
    // Asks the desktop for its wallpaper layer and puts this window in it, below the icons.
    static void Attach(IntPtr me, System.Drawing.Rectangle b)
    {
        var progman = Shell.FindWindow("Progman", null);
        Shell.SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);
        IntPtr worker = IntPtr.Zero;
        Shell.EnumWindows((h, _) => { if (Shell.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) worker = Shell.FindWindowEx(IntPtr.Zero, h, "WorkerW", null); return true; }, IntPtr.Zero);
        var child = Shell.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        Shell.SetWindowLong(me, -16, (Shell.GetWindowLong(me, -16) & ~unchecked((int)0x80C00000)) | 0x40000000); // a child window: no caption, no popup
        if (child != IntPtr.Zero)
        {
            // Windows 11 24H2 and later: the wallpaper layer lives inside Progman, under the icons
            Shell.SetParent(me, progman);
            var icons = Shell.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            Shell.SetWindowPos(me, icons, 0, 0, b.Width, b.Height, 0x0010 | 0x0040);
        }
        else { Shell.SetParent(me, worker != IntPtr.Zero ? worker : progman); Shell.SetWindowPos(me, IntPtr.Zero, 0, 0, b.Width, b.Height, 0x0004 | 0x0010 | 0x0040); }
    }
}

// ───────────────────────── Desktop widgets ─────────────────────────
// Glass tiles that sit on the wallpaper, behind every window, like the day clock. Drag to move.
public class DeskWidget : Window
{
    static readonly Dictionary<string, DeskWidget> Open = new();
    public static readonly (string Id, string Label)[] Kinds = { ("weather", "Weather"), ("system", "System load"), ("music", "Now playing") };
    public static void Sync()
    {
        var want = Settings.Current.DeskWidgets;
        foreach (var k in Open.Keys.Where(k => !want.Contains(k)).ToList()) { Open[k].Close(); Open.Remove(k); }
        foreach (var k in want.Where(k => !Open.ContainsKey(k) && Kinds.Any(x => x.Id == k))) { var w = new DeskWidget(k); Open[k] = w; w.Show(); }
    }
    readonly string _kind; readonly TextBlock _big = T("", 26, "#FFFFFF", true), _sub = T("", 11, "#D1D1D6"); readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromSeconds(2) };
    DeskWidget(string kind)
    {
        _kind = kind; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize; Title = "Perch widget";
        SizeToContent = SizeToContent.WidthAndHeight; _sub.TextTrimming = TextTrimming.CharacterEllipsis; _sub.MaxWidth = 200;
        Content = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(16, 12, 18, 12), MinWidth = 150, Background = B("#59101014"), BorderBrush = B("#40FFFFFF"), BorderThickness = new Thickness(1), Child = Col(_big, _sub.M(0, 2)), Cursor = Cursors.SizeAll };
        var s = Settings.Current;
        if (s.DeskWidgetPos.TryGetValue(kind, out var p) && p.Length == 2) { Left = p[0]; Top = p[1]; }
        else { Left = 80 + Open.Count * 190; Top = SystemParameters.PrimaryScreenHeight - 260; }
        MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } s.DeskWidgetPos[_kind] = new[] { Left, Top }; s.Save(); Shell.ToBottom(this); };
        SourceInitialized += (_, _) => { Shell.NoFocus(this); Shell.ToBottom(this); };
        ContentRendered += (_, _) => Shell.ToBottom(this);
        _t.Tick += (_, _) => Update(); _t.Start(); Closed += (_, _) => _t.Stop(); Update();
    }
    void Update()
    {
        switch (_kind)
        {
            case "weather": { var w = Svc.Weather; _big.Text = w.Ready ? $"{w.Temp:0}°" : "–"; _sub.Text = w.Ready ? $"{w.Condition} · {w.High:0}° / {w.Low:0}°" : "Set a city in Settings"; break; }
            case "system": { var st = Svc.Stats; _big.Text = $"{st.Cpu * 100:0}%"; _sub.Text = $"CPU · RAM {st.Ram * 100:0}% · ↓ {st.DownBps / 1048576:0.0} MB/s"; break; }
            default: { var m = Svc.Media; _big.Text = m.HasSession ? (m.Playing ? "▶" : "❚❚") : "♪"; _big.FontSize = 20; _sub.Text = m.HasSession ? m.Title + " · " + m.Artist : "Nothing playing"; break; }
        }
    }
}
