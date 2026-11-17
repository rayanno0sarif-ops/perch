using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
// Hides the Windows taskbar and shows a floating glass dock: Start, search, your launcher slots, open programs, quick settings and the clock.
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

    // ── one icon in the dock: it magnifies toward the pointer and dips when pressed, both on springs ──
    sealed class Item
    {
        public readonly Border Hit; public readonly ScaleTransform Scale = new(1, 1); public readonly TranslateTransform Lift = new();
        public readonly Spring Mag = new(100) { Response = 0.28, Damping = 1.0 }, Press = new(100) { Response = 0.22, Damping = 1.0 };
        public readonly string Label; public readonly Action Run; public readonly Border Dot;
        public Item(UIElement icon, string label, Action run, bool open, bool front)
        {
            Label = label; Run = run;
            var face = new Grid { Width = 40, Height = 40, RenderTransformOrigin = new Point(0.5, 1), RenderTransform = new TransformGroup { Children = { Scale, Lift } } };
            face.Children.Add(icon);
            Dot = new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), Background = B(front ? "#FFFFFFFF" : "#8CFFFFFF"), Opacity = open ? 1 : 0 };
            Hit = new Border { Background = Brushes.Transparent, Padding = new Thickness(5, 9, 5, 4), Child = new StackPanel { Children = { face, Dot } }, Cursor = Cursors.Hand };
        }
        public bool Settled => Mag.Settled && Press.Settled;
        public void Step(double dt) { Mag.Step(dt); Press.Step(dt); Apply(); }
        public void Apply() { double k = Mag.Value * Press.Value / 10000; Scale.ScaleX = Scale.ScaleY = k; Lift.Y = -(Mag.Value - 100) * 0.42; double room = (Mag.Value - 100) * 0.2; Hit.Margin = new Thickness(room, 0, room, 0); }
    }

    readonly List<Item> _items = new();
    readonly StackPanel _apps = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
    readonly StackPanel _left = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
    readonly TextBlock _time = T("", 12, "#FFFFFF", true), _date = T("", 10, "#C7C7CC");
    readonly Border _bar_, _label = new() { CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 4, 9, 5), Background = B("#E61C1C1E"), BorderBrush = B("#33FFFFFF"), BorderThickness = new Thickness(1), IsHitTestVisible = false, Opacity = 0 };
    readonly TextBlock _labelText = T("", 11.5, "#FFFFFF", true);
    readonly Canvas _overlay = new() { IsHitTestVisible = false };
    readonly TranslateTransform _enter = new(0, 90);
    readonly Spring _rise = new(90) { Response = 0.42, Damping = 1.0 }, _labelOp = new(0) { Response = 0.16, Damping = 1.0 };
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly System.Diagnostics.Stopwatch _clock = new();
    GlassBackdrop? _glass; bool _animating; string _sig = ""; Item? _pressed, _hover; Point? _pointer;
    const double BarH = 66, Room = 46; // room above the bar for magnified icons and the name label

    GlassTaskbar()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; ShowActivated = false; Title = "Perch taskbar";
        Height = BarH + Room + 10; UseLayoutRounding = true;
        _label.Child = _labelText; _overlay.Children.Add(_label);
        var status = new Border { Background = Brushes.Transparent, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(14), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Height = 46,
            Child = Row(Ico("", 13), Ico("", 13).M(10), Ico("", 13).M(10)).With(r => r.VerticalAlignment = VerticalAlignment.Center), ToolTip = "Quick settings" };
        var clock = new Border { Background = Brushes.Transparent, Padding = new Thickness(10, 0, 14, 0), CornerRadius = new CornerRadius(14), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Height = 46,
            Child = Col(_time.With(t => t.HorizontalAlignment = HorizontalAlignment.Right), _date.With(t => t.HorizontalAlignment = HorizontalAlignment.Right)).With(c => c.VerticalAlignment = VerticalAlignment.Center), ToolTip = "Notifications and calendar" };
        Hover(status, () => Hooks.Press(0x5B, 0x41)); Hover(clock, () => Hooks.Press(0x5B, 0x4E));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { _left, Divider(), _apps, Divider(), status, clock } };
        var sheen = new Border { CornerRadius = new CornerRadius(22, 22, 0, 0), Height = 30, VerticalAlignment = VerticalAlignment.Top, Background = Glass.Sheen, IsHitTestVisible = false };
        var inner = new Grid(); inner.Children.Add(sheen); inner.Children.Add(row.With(r => { r.Margin = new Thickness(8, 0, 8, 0); r.VerticalAlignment = VerticalAlignment.Bottom; }));
        _bar_ = new Border
        {
            Height = BarH, CornerRadius = new CornerRadius(22), Background = Glass.On ? Glass.Surface(false) : B("#F0202024"), BorderBrush = Glass.On ? Glass.Rim : Glass.RimSolid, BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10), Child = inner, ClipToBounds = false
        };
        var root = new Grid { RenderTransform = _enter }; root.Children.Add(_bar_); root.Children.Add(_overlay);
        Content = root;
        _bar_.MouseMove += (_, e) => { _pointer = e.GetPosition(_bar_); Magnify(); };
        _bar_.MouseLeave += (_, _) => { _pointer = null; if (_pressed != null) { _pressed.Press.Target = 100; _pressed = null; } Magnify(); };
        _bar_.SizeChanged += (_, _) => Shape();
        SourceInitialized += (_, _) => { Shell.NoFocus(this); if (Glass.On) { _glass = new GlassBackdrop(this) { Reach = 0.75f }; Shape(); _glass.Follow(); } };
        Loaded += (_, _) => { _rise.Target = 0; if (S.NoMotion) _rise.Snap(); Animate(); };
        _tick.Tick += (_, _) => Clock(); _tick.Start(); Closed += (_, _) => { _tick.Stop(); CompositionTarget.Rendering -= Frame; };
        Clock(); Refresh(); Place();
    }
    static Border Divider() => new() { Width = 1, Height = 30, Background = B("#2EFFFFFF"), Margin = new Thickness(7, 0, 7, 18), VerticalAlignment = VerticalAlignment.Bottom };
    // a plain hover wash for the text areas on the right; it responds on press, not on release
    static void Hover(Border b, Action run)
    {
        b.MouseEnter += (_, _) => b.Background = B("#1FFFFFFF"); b.MouseLeave += (_, _) => { b.Background = Brushes.Transparent; b.Opacity = 1; };
        b.MouseLeftButtonDown += (_, _) => b.Opacity = 0.7; b.MouseLeftButtonUp += (_, _) => { b.Opacity = 1; run(); };
    }
    void Place() { Left = 0; Width = SystemParameters.PrimaryScreenWidth; Top = SystemParameters.PrimaryScreenHeight - Height; }
    void Shape()
    {
        if (_glass == null || _bar_.ActualWidth < 1) return;
        var at = _bar_.TranslatePoint(new Point(0, 0), (UIElement)Content);
        _glass.SetShape(at.X, at.Y + _enter.Y, _bar_.ActualWidth, _bar_.ActualHeight, 22);
    }
    void Clock() { var n = DateTime.Now; _time.Text = UI.TimeText(n); _date.Text = n.ToString("ddd d MMM"); }

    // Icons near the pointer grow and rise; the ones further away settle back. The falloff is a cosine over about two icons on each side.
    void Magnify()
    {
        Item? hover = null;
        foreach (var it in _items)
        {
            double target = 100;
            if (_pointer is Point p && !S.NoMotion)
            {
                var c = it.Hit.TranslatePoint(new Point(it.Hit.ActualWidth / 2, 0), _bar_).X; double d = Math.Abs(p.X - c);
                if (d < 110) target = 100 + 34 * Math.Cos(d / 110 * Math.PI / 2);
                if (d < it.Hit.ActualWidth / 2) hover = it;
            }
            it.Mag.Target = target;
        }
        if (_pointer is Point pp) { foreach (var it in _items) if (it.Hit.IsMouseOver) hover = it; }
        if (hover != _hover)
        {
            _hover = hover;
            if (hover != null) { _labelText.Text = hover.Label; _label.UpdateLayout(); }
            _labelOp.Target = hover != null ? 100 : 0;
        }
        Animate();
    }
    void Animate() { if (_animating) return; _animating = true; _clock.Restart(); CompositionTarget.Rendering += Frame; }
    void Frame(object? s, EventArgs e)
    {
        double dt = Math.Min(0.05, _clock.Elapsed.TotalSeconds); _clock.Restart();
        bool busy = false;
        foreach (var it in _items) { if (S.NoMotion) { it.Mag.Snap(); it.Press.Snap(); it.Apply(); } else it.Step(dt); busy |= !it.Settled; }
        _rise.Step(dt); _labelOp.Step(dt); busy |= !_rise.Settled || !_labelOp.Settled;
        _enter.Y = _rise.Value; Opacity = Math.Clamp(1 - _rise.Value / 90, 0, 1);
        _label.Opacity = _labelOp.Value / 100;
        if (_hover != null)
        {
            var c = _hover.Hit.TranslatePoint(new Point(_hover.Hit.ActualWidth / 2, 0), _overlay);
            Canvas.SetLeft(_label, c.X - _label.ActualWidth / 2); Canvas.SetTop(_label, c.Y - 40 - (_hover.Mag.Value - 100) * 0.42);
        }
        Shape();
        if (!busy) { _animating = false; CompositionTarget.Rendering -= Frame; Shape(); }
    }

    Item MakeItem(UIElement icon, string label, Action run, bool open = false, bool front = false)
    {
        var it = new Item(icon, label, run, open, front);
        it.Hit.MouseLeftButtonDown += (_, e) => { _pressed = it; it.Press.Target = 86; Animate(); e.Handled = true; }; // feedback on press
        it.Hit.MouseLeftButtonUp += (_, e) => { if (_pressed == it) { it.Press.Target = 100; _pressed = null; Animate(); it.Run(); _sig = ""; } e.Handled = true; };
        _items.Add(it); return it;
    }
    static UIElement WindowsMark()
    {
        var g = new UniformGrid { Rows = 2, Columns = 2, Width = 17, Height = 17 };
        for (int i = 0; i < 4; i++) g.Children.Add(new Border { Background = B("#E6FFFFFF"), Margin = new Thickness(0.8), CornerRadius = new CornerRadius(1.5) });
        return new Border { CornerRadius = new CornerRadius(11), Background = B("#26FFFFFF"), Child = g };
    }
    static UIElement Glyph(string g) => new Border { CornerRadius = new CornerRadius(11), Background = B("#26FFFFFF"), Child = Ico(g, 17) };

    // Launcher slots first, then programs with open windows that are not already a slot.
    public void Refresh()
    {
        var running = WinList.List(true).GroupBy(w => w.Path, StringComparer.OrdinalIgnoreCase).Where(g => g.Key.Length > 0).ToList();
        var slots = S.Slots.Where(p => p.Length > 0 && File.Exists(p)).ToList();
        string sig = string.Join("|", slots) + "#" + string.Join("|", running.Select(g => g.Key + g.Count() + g.Any(w => w.Handle == Fg())));
        if (sig == _sig) return; _sig = sig;
        _items.Clear(); _left.Children.Clear(); _apps.Children.Clear(); _hover = null; _labelOp.Target = 0;
        _left.Children.Add(MakeItem(WindowsMark(), "Start", () => Hooks.Press(0x5B)).Hit);
        _left.Children.Add(MakeItem(Glyph(""), "Search", () => Hooks.Press(0x5B, 0x53)).Hit);
        foreach (var p in slots) _apps.Children.Add(AppItem(p, running.FirstOrDefault(g => g.Key.Equals(p, StringComparison.OrdinalIgnoreCase))?.ToList()));
        foreach (var g in running.Where(g => !slots.Contains(g.Key, StringComparer.OrdinalIgnoreCase))) _apps.Children.Add(AppItem(g.Key, g.ToList()));
        if (_pointer != null) Magnify();
    }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    static IntPtr Fg() => GetForegroundWindow();
    UIElement AppItem(string path, List<WinInfo>? wins)
    {
        bool open = wins is { Count: > 0 }, front = open && wins!.Any(w => w.Handle == Fg());
        var img = new Image { Source = Icons.Large(path), Width = 40, Height = 40 }; RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        string name = Path.GetFileNameWithoutExtension(path); try { var d = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileDescription; if (!string.IsNullOrWhiteSpace(d) && d.Length < 40) name = d.Trim(); } catch { }
        var it = MakeItem(img, name, () =>
        {
            if (!open) { Svc.Open(path); return; }
            var w = wins![0];
            if (front && !IsIconic(w.Handle)) Shell.ShowWindow(w.Handle, 6); else WinList.Activate(w.Handle); // clicking the program in front minimises it, like Windows
        }, open, front);
        if (open) it.Hit.MouseRightButtonUp += (_, e) => { foreach (var w in wins!) WinList.Close(w.Handle); e.Handled = true; _sig = ""; };
        return it.Hit;
    }
}

// Large, sharp program icons (the shell's own 64 px image), so a magnified icon stays crisp.
static class Icons
{
    static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr bmp); }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)] static extern void SHCreateItemFromParsingName(string path, IntPtr bc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItemImageFactory f);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, out BITMAP b);
    public static ImageSource? Large(string path)
    {
        if (Cache.TryGetValue(path, out var c)) return c;
        ImageSource? src = null;
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var f);
            if (f.GetImage(new SIZE { cx = 64, cy = 64 }, 0x1 /* icon only, scale up */, out var hbmp) == 0)
            {
                // read the bits directly: CreateBitmapSourceFromHBitmap drops the alpha channel and gives the icon a black square
                try { if (GetObject(hbmp, Marshal.SizeOf<BITMAP>(), out var bm) != 0 && bm.bmBitsPixel == 32 && bm.bmBits != IntPtr.Zero) { var bs = BitmapSource.Create(bm.bmWidth, bm.bmHeight, 96, 96, PixelFormats.Pbgra32, null, bm.bmBits, bm.bmWidthBytes * bm.bmHeight, bm.bmWidthBytes); bs.Freeze(); src = bs; } }
                finally { DeleteObject(hbmp); }
            }
        }
        catch { }
        src ??= Native.FileIcon(path);
        return Cache[path] = src;
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
