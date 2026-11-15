using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;
using static Perch.UI;

namespace Perch;

public partial class Settings
{
    public string ShotFolder { get; set; } = "";                 // empty is Pictures\Perch
    public string ShotSubfolder { get; set; } = "";              // a date pattern, like %y-%mo
    public string ShotPattern { get; set; } = "Screenshot %y-%mo-%d %h.%mi.%s";
    public string ShotAction { get; set; } = "editor";           // editor | savecopy | copy | save
    public int ShotDelay { get; set; }                           // seconds before the screen is read
    public bool ShotPointer { get; set; }                        // draw the pointer into the picture
    public bool ShotAddToShelf { get; set; } = true;
    public string ShotLastRegion { get; set; } = "";
    public string ShotBackdrop { get; set; } = "none";           // none | sunset | ocean | graphite | mist
    public string ShotWatermark { get; set; } = "";
    public int ShotNext { get; set; } = 1;
    public string ShotTool { get; set; } = "arrow";
    public string ShotColour { get; set; } = "#FF3B30";
    public int ShotStroke { get; set; } = 4;
    public bool ShotScrollAuto { get; set; } = true;
}

public static class Shot
{
    [DllImport("user32.dll")] static extern bool GetCursorPos(out System.Drawing.Point p);
    [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int w, int h, int step, IntPtr brush, int flags);
    [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO ci);
    [StructLayout(LayoutKind.Sequential)] struct CURSORINFO { public int Size, Flags; public IntPtr Cursor; public System.Drawing.Point Pos; }

    public static string Folder
    {
        get
        {
            var s = Settings.Current; var dir = s.ShotFolder.Length > 0 ? s.ShotFolder : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Perch");
            if (s.ShotSubfolder.Length > 0) dir = Path.Combine(dir, Pattern(s.ShotSubfolder));
            return dir;
        }
    }
    // %y year (2 digits) %year %mo month number %month name %d day %h hour %mi minute %s second %# an increasing number
    public static string Pattern(string p)
    {
        var n = DateTime.Now;
        p = p.Replace("%year", n.ToString("yyyy")).Replace("%month", n.ToString("MMMM")).Replace("%mo", n.ToString("MM")).Replace("%mi", n.ToString("mm")).Replace("%y", n.ToString("yy")).Replace("%d", n.ToString("dd")).Replace("%h", n.ToString("HH")).Replace("%s", n.ToString("ss"));
        p = System.Text.RegularExpressions.Regex.Replace(p, "%(#+)", m => Settings.Current.ShotNext.ToString().PadLeft(m.Groups[1].Length, '0'));
        foreach (var c in Path.GetInvalidFileNameChars()) if (c != '\\' && c != '/') p = p.Replace(c.ToString(), "");
        return p;
    }
    public static string NewFile()
    {
        var dir = Folder; Directory.CreateDirectory(dir);
        string stem = Pattern(Settings.Current.ShotPattern); if (stem.Length == 0) stem = "Screenshot";
        string f = Path.Combine(dir, stem + ".png"); int i = 2;
        while (File.Exists(f)) f = Path.Combine(dir, $"{stem} ({i++}).png");
        if (Settings.Current.ShotPattern.Contains("%#")) { Settings.Current.ShotNext++; Settings.Current.Save(); }
        return f;
    }

    public static System.Drawing.Bitmap Grab(System.Drawing.Rectangle r, bool pointer)
    {
        var bmp = new System.Drawing.Bitmap(r.Width, r.Height);
        using var g = System.Drawing.Graphics.FromImage(bmp); g.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size);
        if (pointer)
        {
            var ci = new CURSORINFO { Size = Marshal.SizeOf<CURSORINFO>() };
            if (GetCursorInfo(ref ci) && ci.Flags == 1) { var dc = g.GetHdc(); DrawIconEx(dc, ci.Pos.X - r.Left, ci.Pos.Y - r.Top, ci.Cursor, 0, 0, 0, IntPtr.Zero, 3); g.ReleaseHdc(dc); }
        }
        return bmp;
    }
    public static BitmapSource ToSource(System.Drawing.Bitmap bmp)
    {
        var bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var src = BitmapSource.Create(bmp.Width, bmp.Height, 96, 96, PixelFormats.Bgra32, null, bd.Scan0, bd.Stride * bmp.Height, bd.Stride);
        bmp.UnlockBits(bd); src.Freeze(); return src;
    }
    public static void Save(BitmapSource img, string file) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(img)); using var fs = File.Create(file); enc.Save(fs); }
    public static void Copy(BitmapSource img) { try { Clipboard.SetImage(img); } catch { } }

    // Starts a capture: mode is area, window or full.
    public static async void Start(string mode = "area")
    {
        IslandWindow.Instance.Vanish(true);
        int delay = Settings.Current.ShotDelay; if (delay > 0) { for (int i = delay; i > 0; i--) { Svc.Notices.Add("Capturing in " + i, "", ""); await Task.Delay(1000); } }
        await Task.Delay(160);
        try
        {
            var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            if (mode == "full")
            {
                var scr = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).Bounds;
                using var b = Grab(scr, Settings.Current.ShotPointer); IslandWindow.Instance.Vanish(false); Finish(ToSource(b)); return;
            }
            var frozen = Grab(vs, false); var wins = WinList.List(false).Select(w => w.Handle).ToList();
            new ShotSelect(frozen, vs, mode == "window").Show();
        }
        catch (Exception e) { Svc.Notices.Add("The screen could not be captured", e.Message.Length > 60 ? e.Message[..60] : e.Message, ""); }
        finally { IslandWindow.Instance.Vanish(false); }
    }
    public static void Finish(BitmapSource img)
    {
        var s = Settings.Current;
        switch (s.ShotAction)
        {
            case "savecopy": { var f = NewFile(); Save(img, f); Copy(img); After(f); Svc.Notices.Add("Screenshot saved and copied", Path.GetFileName(f), ""); break; }
            case "copy": Copy(img); Svc.Notices.Add("Screenshot copied", "", ""); break;
            case "save": { var f = NewFile(); Save(img, f); After(f); Svc.Notices.Add("Screenshot saved", Path.GetFileName(f), ""); break; }
            default: new ShotEditor(img).Show(); break;
        }
    }
    public static void After(string file) { if (Settings.Current.ShotAddToShelf && Settings.Current.CapturesOnShelf) Svc.AddToShelf(new[] { file }); }
}

// Pick an area, or click a window, on a frozen picture of the screen.
public class ShotSelect : Window
{
    readonly System.Drawing.Bitmap _frozen; readonly System.Drawing.Rectangle _vs; readonly bool _windowMode;
    readonly Canvas _c = new(); readonly Border _sel = new() { BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    readonly Border _hint, _hover = new() { BorderBrush = B("#0A84FF"), BorderThickness = new Thickness(3), Background = B("#220A84FF"), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    readonly TextBlock _size = T("", 11, "#FFFFFF", true, true), _hintText;
    readonly System.Windows.Shapes.Path _dim = new() { Fill = B("#88000000"), IsHitTestVisible = false };
    Point? _from; bool _scrolling; List<(IntPtr H, System.Drawing.Rectangle R)> _rects = new();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    readonly Action<System.Drawing.Rectangle>? _pick;
    public ShotSelect(System.Drawing.Bitmap frozen, System.Drawing.Rectangle vs, bool windowMode, Action<System.Drawing.Rectangle>? pick = null)
    {
        _frozen = frozen; _vs = vs; _windowMode = windowMode; _pick = pick;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; Title = "Perch capture"; Background = Brushes.Black; Cursor = Cursors.Cross;
        Left = SystemParameters.VirtualScreenLeft; Top = SystemParameters.VirtualScreenTop; Width = SystemParameters.VirtualScreenWidth; Height = SystemParameters.VirtualScreenHeight;
        var img = new Image { Source = Shot.ToSource(frozen), Stretch = Stretch.Fill, IsHitTestVisible = false };
        _hintText = T(windowMode ? "Click a window  ·  Esc cancels" : "Drag an area  ·  click a window  ·  Enter for the whole screen  ·  R repeats the last area  ·  S scrolling capture  ·  Esc cancels", 11.5, "#FFFFFF", true);
        _hint = new Border { Background = B("#E6161618"), BorderBrush = Glass.Rim, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(12, 7, 12, 7), Child = _hintText, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left };
        _c.Children.Add(img); _c.Children.Add(_dim); _c.Children.Add(_hover); _c.Children.Add(_sel); _c.Children.Add(_size); _c.Children.Add(_hint);
        Content = _c;
        // windows under the pointer, front to back
        foreach (var w in WinList.List(false))
        {
            if (DwmGetWindowAttribute(w.Handle, 9, out var r, Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(w.Handle, out r)) continue;
            _rects.Add((w.Handle, new System.Drawing.Rectangle(r.L, r.T, r.R - r.L, r.B - r.T)));
        }
        Loaded += (_, _) => { Activate(); Focus(); Canvas.SetLeft(_hint, 24); Canvas.SetTop(_hint, 24); Dim(null); };
        Closed += (_, _) => _frozen.Dispose();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) { var scr = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).Bounds; Done(scr); }
            else if (e.Key == Key.R && Settings.Current.ShotLastRegion.Length > 0) { var p = Settings.Current.ShotLastRegion.Split(','); if (p.Length == 4) Done(new System.Drawing.Rectangle(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]))); }
            else if (e.Key == Key.S) { _scrolling = !_scrolling; _hintText.Text = _scrolling ? "Scrolling capture: drag only the part of the page that moves  ·  S turns it off" : "Drag an area  ·  S scrolling capture  ·  Esc cancels"; }
        };
        MouseRightButtonUp += (_, _) => Close();
        MouseMove += (_, e) => Move(e.GetPosition(this));
        MouseLeftButtonDown += (_, e) => { _from = e.GetPosition(this); CaptureMouse(); };
        MouseLeftButtonUp += (_, e) =>
        {
            if (_from == null) return; ReleaseMouseCapture();
            var a = _from.Value; var b = e.GetPosition(this); _from = null;
            if (Math.Abs(a.X - b.X) < 6 && Math.Abs(a.Y - b.Y) < 6)
            {
                var p = PointToScreen(b); var hit = _rects.FirstOrDefault(r => r.R.Contains((int)p.X, (int)p.Y));
                if (hit.H != IntPtr.Zero) Done(hit.R); else if (!_windowMode) Close();
                return;
            }
            if (_windowMode) return;
            var pa = PointToScreen(a); var pb = PointToScreen(b);
            Done(new System.Drawing.Rectangle((int)Math.Min(pa.X, pb.X), (int)Math.Min(pa.Y, pb.Y), (int)Math.Abs(pa.X - pb.X), (int)Math.Abs(pa.Y - pb.Y)));
        };
    }
    void Dim(Rect? hole)
    {
        var all = new RectangleGeometry(new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)));
        _dim.Data = hole == null ? all : new CombinedGeometry(GeometryCombineMode.Exclude, all, new RectangleGeometry(hole.Value));
    }
    void Move(Point p)
    {
        if (_from == null)
        {
            if (_windowMode || true)
            {
                var s = PointToScreen(p); var hit = _rects.FirstOrDefault(r => r.R.Contains((int)s.X, (int)s.Y));
                if (hit.H != IntPtr.Zero)
                {
                    var tl = PointFromScreen(new Point(hit.R.Left, hit.R.Top)); var br = PointFromScreen(new Point(hit.R.Right, hit.R.Bottom));
                    _hover.Visibility = Visibility.Visible; Canvas.SetLeft(_hover, tl.X); Canvas.SetTop(_hover, tl.Y); _hover.Width = Math.Max(0, br.X - tl.X); _hover.Height = Math.Max(0, br.Y - tl.Y);
                }
                else _hover.Visibility = Visibility.Collapsed;
            }
            return;
        }
        _hover.Visibility = Visibility.Collapsed;
        if (_windowMode) return;
        double x = Math.Min(p.X, _from.Value.X), y = Math.Min(p.Y, _from.Value.Y), w = Math.Abs(p.X - _from.Value.X), h = Math.Abs(p.Y - _from.Value.Y);
        _sel.Visibility = Visibility.Visible; Canvas.SetLeft(_sel, x); Canvas.SetTop(_sel, y); _sel.Width = w; _sel.Height = h; Dim(new Rect(x, y, w, h));
        var d = PointToScreen(new Point(x + w, y + h)); var o = PointToScreen(new Point(x, y));
        _size.Text = $"{(int)(d.X - o.X)} × {(int)(d.Y - o.Y)}"; Canvas.SetLeft(_size, x + 2); Canvas.SetTop(_size, Math.Max(2, y - 20));
    }
    async void Done(System.Drawing.Rectangle area)
    {
        area.Intersect(_vs);
        if (area.Width < 4 || area.Height < 4) { Close(); return; }
        if (_pick != null) { Close(); _pick(area); return; }
        Settings.Current.ShotLastRegion = $"{area.X},{area.Y},{area.Width},{area.Height}"; Settings.Current.Save();
        var crop = _frozen.Clone(new System.Drawing.Rectangle(area.X - _vs.Left, area.Y - _vs.Top, area.Width, area.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        bool scrolling = _scrolling; Close();
        if (scrolling) { crop.Dispose(); await ScrollingCapture.Run(area); return; }
        var src = Shot.ToSource(crop); crop.Dispose();
        Shot.Finish(src);
    }
}

// A scrolling screenshot: Perch reads the area as it scrolls, finds where each new picture continues the last one, and joins them.
public static class ScrollingCapture
{
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, int dx, int dy, int data, UIntPtr extra);
    static byte[] Px(System.Drawing.Bitmap b, out int stride)
    {
        var bd = b.LockBits(new System.Drawing.Rectangle(0, 0, b.Width, b.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bytes = new byte[bd.Stride * b.Height]; Marshal.Copy(bd.Scan0, bytes, 0, bytes.Length); stride = bd.Stride; b.UnlockBits(bd); return bytes;
    }
    static long RowHash(byte[] p, int stride, int y, int w) { long h = 1469598103934665603; int o = y * stride; for (int x = 0; x < w * 4; x += 4) { h = (h ^ p[o + x]) * 1099511628211; h = (h ^ p[o + x + 1]) * 1099511628211; h = (h ^ p[o + x + 2]) * 1099511628211; } return h; }
    public static async Task Run(System.Drawing.Rectangle area)
    {
        bool auto = Settings.Current.ShotScrollAuto;
        Svc.Notices.Add(auto ? "Scrolling capture" : "Scroll the page now", auto ? "Perch scrolls the area and stops at the bottom." : "Press Enter, or wait for it to stop, when you are done.", "");
        IslandWindow.Instance.Vanish(true); await Task.Delay(300);
        var frames = new List<System.Drawing.Bitmap>(); var pieces = new List<(System.Drawing.Bitmap Bmp, int From)>();
        System.Drawing.Bitmap? prev = null; int stable = 0; long total = 0; int guard = 0;
        if (auto) { SetCursorPos(area.Left + area.Width / 2, area.Top + area.Height / 2); }
        while (guard++ < 120 && total < 40000)
        {
            var cur = Shot.Grab(area, false);
            if (prev == null) { pieces.Add((cur, 0)); prev = cur; total += cur.Height; }
            else
            {
                var pa = Px(prev, out int st); var pb = Px(cur, out _); int w = Math.Min(prev.Width, cur.Width), h = cur.Height;
                var ha = new long[prev.Height]; for (int y = 0; y < prev.Height; y++) ha[y] = RowHash(pa, st, y, w);
                var hb = new long[h]; for (int y = 0; y < h; y++) hb[y] = RowHash(pb, st, y, w);
                if (hb.SequenceEqual(ha)) { stable++; cur.Dispose(); if (stable >= (auto ? 1 : 6)) break; }
                else
                {
                    stable = 0; int best = -1;
                    // how far did the content move up: find the largest shift where the rest of the old picture lines up with the top of the new one
                    for (int shift = 1; shift < prev.Height - 20; shift++) { int len = prev.Height - shift; bool ok = true; for (int y = 0; y < len; y += Math.Max(1, len / 40)) if (ha[shift + y] != hb[y]) { ok = false; break; } if (ok) { best = shift; break; } }
                    if (best < 0) { cur.Dispose(); break; }
                    int newRows = cur.Height - (prev.Height - best); if (newRows <= 0) { cur.Dispose(); continue; }
                    pieces.Add((cur, cur.Height - newRows)); total += newRows; prev = cur;
                }
            }
            if (auto) { mouse_event(0x0800, 0, 0, -(int)(area.Height * 0.6 / 120 * 120 / 3 * 1) * 1, UIntPtr.Zero); await Task.Delay(550); }
            else await Task.Delay(400);
        }
        IslandWindow.Instance.Vanish(false);
        if (pieces.Count == 0) return;
        int width = pieces[0].Bmp.Width; var outBmp = new System.Drawing.Bitmap(width, (int)total); int at = 0;
        using (var g = System.Drawing.Graphics.FromImage(outBmp))
            foreach (var (b, from) in pieces) { g.DrawImage(b, new System.Drawing.Rectangle(0, at, width, b.Height - from), new System.Drawing.Rectangle(0, from, width, b.Height - from), System.Drawing.GraphicsUnit.Pixel); at += b.Height - from; }
        foreach (var (b, _) in pieces) b.Dispose();
        var src = Shot.ToSource(outBmp); outBmp.Dispose();
        Shot.Finish(src);
    }
}

// The editor: annotate, crop, hide, add a background, then copy, save or pin.
public class ShotEditor : Window
{
    BitmapSource _img; readonly Canvas _canvas = new(); readonly Image _view = new(); readonly Grid _stage = new(); readonly List<UIElement> _undo = new();
    string _tool; Color _colour; double _stroke; Point _p0; UIElement? _live; Polyline? _pen; int _count = 1; bool _drawing;
    static readonly (string Id, string Label, string Glyph)[] Tools =
    {
        ("arrow", "Arrow", ""), ("line", "Line", ""), ("rect", "Rectangle", ""), ("ellipse", "Ellipse", ""), ("pen", "Pen", ""), ("highlight", "Highlighter", ""),
        ("text", "Text", ""), ("number", "Number", ""), ("pixelate", "Pixelate", ""), ("redact", "Solid block", ""), ("spotlight", "Spotlight", ""), ("crop", "Crop", "")
    };
    static readonly string[] Palette = { "#FF3B30", "#FF9F0A", "#FFD60A", "#30D158", "#0A84FF", "#BF5AF2", "#FFFFFF", "#000000" };
    WrapPanel _toolBar = new(); WrapPanel _colours = new();

    public ShotEditor(BitmapSource img)
    {
        _img = img; var s = Settings.Current; _tool = s.ShotTool; _colour = (Color)ColorConverter.ConvertFromString(s.ShotColour); _stroke = s.ShotStroke;
        Title = "Perch screenshot"; Width = Math.Min(1200, Math.Max(640, img.PixelWidth + 80)); Height = Math.Min(860, Math.Max(520, img.PixelHeight + 190));
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.CanResizeWithGrip; UseLayoutRounding = true; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _view.Source = _img; _view.Stretch = Stretch.None; _canvas.Width = _img.PixelWidth; _canvas.Height = _img.PixelHeight; _canvas.Background = Brushes.Transparent;
        _stage.Width = _img.PixelWidth; _stage.Height = _img.PixelHeight; _stage.Children.Add(_view); _stage.Children.Add(_canvas);
        var vb = new Viewbox { Child = _stage, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(14) };
        var head = MakeGrid("*,auto", "auto"); head.Margin = new Thickness(16, 10, 10, 6); head.Background = Brushes.Transparent;
        head.Children.Add(T("Screenshot", 12.5, "#FFFFFF", true).With(x => x.VerticalAlignment = VerticalAlignment.Center).At(0, 0));
        head.Children.Add(IconBtn("", Close, 10, "#C7C7CC", 28).At(0, 1));
        head.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        BuildBars();
        var bottom = Row(Pill("Copy", () => { Shot.Copy(Render()); Svc.Notices.Add("Screenshot copied", "", ""); }, true), Pill("Save", () => { var f = Shot.NewFile(); Shot.Save(Render(), f); Shot.After(f); Svc.Notices.Add("Screenshot saved", Path.GetFileName(f), ""); }).M(6),
            Pill("Save as…", () => { var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PNG|*.png|JPEG|*.jpg", FileName = Path.GetFileName(Shot.NewFile()) }; if (dlg.ShowDialog() == true) { var r = Render(); if (dlg.FileName.EndsWith(".jpg")) { var enc = new JpegBitmapEncoder { QualityLevel = 92 }; enc.Frames.Add(BitmapFrame.Create(r)); using var fs = File.Create(dlg.FileName); enc.Save(fs); } else Shot.Save(r, dlg.FileName); } }).M(6),
            Pill("Pin to screen", () => { new ShotPin(Render()).Show(); Close(); }).M(6), Pill("Copy text", async () => { await Tools2.ReadImageText(Render()); }).M(6),
            Pill("Add to shelf", () => { var f = Shot.NewFile(); Shot.Save(Render(), f); Svc.AddToShelf(new[] { f }); }).M(6), Pill("Undo", Undo).M(6));
        bottom.Margin = new Thickness(16, 6, 16, 12);
        var root = MakeGrid("*", "auto,auto,auto,*,auto");
        root.Children.Add(head.At(0, 0)); root.Children.Add(_toolBar.With(t => t.Margin = new Thickness(14, 0, 14, 4)).At(1, 0)); root.Children.Add(_colours.With(t => t.Margin = new Thickness(14, 2, 14, 4)).At(2, 0));
        root.Children.Add(Box(vb, "#14FFFFFF", 12, new Thickness(0)).M(14, 4, 14, 4).At(3, 0)); root.Children.Add(bottom.At(4, 0));
        Content = new Border { Background = B("#F01C1C1E"), CornerRadius = new CornerRadius(16), BorderBrush = Glass.Rim, BorderThickness = new Thickness(1), Child = root, Margin = new Thickness(10),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 26, ShadowDepth = 5, Opacity = 0.5, Color = Colors.Black } };
        _canvas.MouseLeftButtonDown += Down; _canvas.MouseMove += Moved; _canvas.MouseLeftButtonUp += Up;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) Undo(); else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && !(Keyboard.FocusedElement is TextBox)) Shot.Copy(Render()); };
    }
    void BuildBars()
    {
        _toolBar.Children.Clear(); _colours.Children.Clear();
        foreach (var (id, label, glyph) in Tools)
        {
            var tid = id; bool on = _tool == tid;
            var b = Btn(Row(Ico(glyph, 13, on ? "#000000" : "#FFFFFF"), T(label, 10.5, on ? "#000000" : "#E5E5EA", true).M(5, 0, 0, 0)), () => { _tool = tid; Settings.Current.ShotTool = tid; Settings.Current.Save(); BuildBars(); }, on ? "#FFFFFF" : Card, 9, new Thickness(8, 5, 9, 5));
            b.Margin = new Thickness(0, 0, 4, 4); _toolBar.Children.Add(b);
        }
        foreach (var hex in Palette)
        {
            var h = hex; bool on = string.Equals(Settings.Current.ShotColour, h, StringComparison.OrdinalIgnoreCase);
            var dot = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = B(h), BorderBrush = on ? Brushes.White : B("#55FFFFFF"), BorderThickness = new Thickness(on ? 2.5 : 1), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand };
            dot.MouseLeftButtonUp += (_, _) => { _colour = (Color)ColorConverter.ConvertFromString(h); Settings.Current.ShotColour = h; Settings.Current.Save(); BuildBars(); };
            _colours.Children.Add(dot);
        }
        _colours.Children.Add(T("Thickness", 10.5, Dim).M(10, 0, 6, 0).With(x => x.VerticalAlignment = VerticalAlignment.Center));
        foreach (var w in new[] { 2, 4, 7, 12 })
        {
            int ww = w; bool on = (int)_stroke == ww;
            var b = Btn(new Border { Width = 18, Height = Math.Max(2, ww), Background = on ? Brushes.Black : Brushes.White, CornerRadius = new CornerRadius(ww / 2.0) }, () => { _stroke = ww; Settings.Current.ShotStroke = ww; Settings.Current.Save(); BuildBars(); }, on ? "#FFFFFF" : Card, 8, new Thickness(6, 6, 6, 6));
            b.Margin = new Thickness(0, 0, 4, 0); _colours.Children.Add(b);
        }
        string[] bd = { "none", "sunset", "ocean", "graphite", "mist" };
        _colours.Children.Add(T("Background", 10.5, Dim).M(12, 0, 6, 0).With(x => x.VerticalAlignment = VerticalAlignment.Center));
        foreach (var k in bd) { var kk = k; bool on = Settings.Current.ShotBackdrop == kk; _colours.Children.Add(Btn(T(kk == "none" ? "None" : char.ToUpper(kk[0]) + kk[1..], 10.5, on ? "#000000" : "#E5E5EA", true), () => { Settings.Current.ShotBackdrop = kk; Settings.Current.Save(); BuildBars(); }, on ? "#FFFFFF" : Card, 8, new Thickness(8, 4, 8, 4)).M(0, 0, 4, 0)); }
    }
    Brush Stroke => new SolidColorBrush(_colour);
    void Add(UIElement e) { _canvas.Children.Add(e); _undo.Add(e); }
    void Undo() { if (_undo.Count == 0) return; var e = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); _canvas.Children.Remove(e); }
    static Geometry Arrow(Point a, Point b, double w)
    {
        var g = new StreamGeometry(); var v = b - a; if (v.Length < 2) return g; v.Normalize(); var n = new Vector(-v.Y, v.X); double head = Math.Max(12, w * 4);
        using (var c = g.Open())
        {
            c.BeginFigure(a, false, false); c.LineTo(b, true, false);
            c.BeginFigure(b - v * head + n * head * 0.55, false, false); c.LineTo(b, true, false); c.LineTo(b - v * head - n * head * 0.55, true, false);
        }
        g.Freeze(); return g;
    }
    void Down(object s, MouseButtonEventArgs e)
    {
        _p0 = e.GetPosition(_canvas); _drawing = true; _canvas.CaptureMouse();
        switch (_tool)
        {
            case "arrow": _live = new System.Windows.Shapes.Path { Stroke = Stroke, StrokeThickness = _stroke, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }; break;
            case "line": _live = new Line { Stroke = Stroke, StrokeThickness = _stroke, X1 = _p0.X, Y1 = _p0.Y, X2 = _p0.X, Y2 = _p0.Y, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }; break;
            case "rect": case "pixelate": case "redact": case "spotlight": case "crop": _live = new Rectangle { Stroke = _tool == "redact" || _tool == "pixelate" ? B("#88FFFFFF") : _tool == "crop" ? Brushes.White : Stroke, StrokeThickness = _tool is "rect" ? _stroke : 1.5, StrokeDashArray = _tool == "crop" ? new DoubleCollection { 4, 3 } : null, Fill = _tool == "redact" ? Brushes.Black : null, RadiusX = _tool == "rect" ? 3 : 0, RadiusY = _tool == "rect" ? 3 : 0 }; break;
            case "ellipse": _live = new Ellipse { Stroke = Stroke, StrokeThickness = _stroke }; break;
            case "pen": _pen = new Polyline { Stroke = Stroke, StrokeThickness = _stroke, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }; _pen.Points.Add(_p0); _live = _pen; break;
            case "highlight": _pen = new Polyline { Stroke = new SolidColorBrush(Color.FromArgb(110, _colour.R, _colour.G, _colour.B)), StrokeThickness = Math.Max(14, _stroke * 4), StrokeLineJoin = PenLineJoin.Round }; _pen.Points.Add(_p0); _live = _pen; break;
            case "number":
                {
                    var n = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Stroke, Child = T((_count++).ToString(), 13, _colour.R * 0.3 + _colour.G * 0.59 + _colour.B * 0.11 > 150 ? "#000000" : "#FFFFFF", true).With(x => { x.HorizontalAlignment = HorizontalAlignment.Center; x.VerticalAlignment = VerticalAlignment.Center; }) };
                    Canvas.SetLeft(n, _p0.X - 14); Canvas.SetTop(n, _p0.Y - 14); Add(n); _drawing = false; _canvas.ReleaseMouseCapture(); return;
                }
            case "text":
                {
                    var tb = new TextBox { Foreground = Stroke, Background = Brushes.Transparent, BorderBrush = B("#88FFFFFF"), BorderThickness = new Thickness(1), FontSize = 14 + _stroke * 2, FontWeight = FontWeights.SemiBold, FontFamily = Font, MinWidth = 60, AcceptsReturn = true, CaretBrush = Stroke };
                    Canvas.SetLeft(tb, _p0.X); Canvas.SetTop(tb, _p0.Y); Add(tb); tb.Focus(); tb.LostFocus += (_, _) => { tb.BorderThickness = new Thickness(0); if (tb.Text.Length == 0) { _canvas.Children.Remove(tb); _undo.Remove(tb); } };
                    _drawing = false; _canvas.ReleaseMouseCapture(); return;
                }
        }
        if (_live != null) { if (_live is System.Windows.Shapes.Path || _live is Rectangle || _live is Ellipse) { Canvas.SetLeft(_live, _p0.X); Canvas.SetTop(_live, _p0.Y); } _canvas.Children.Add(_live); }
    }
    void Moved(object s, MouseEventArgs e)
    {
        if (!_drawing || _live == null) return;
        var p = e.GetPosition(_canvas);
        switch (_live)
        {
            case System.Windows.Shapes.Path path: { Canvas.SetLeft(path, 0); Canvas.SetTop(path, 0); path.Data = Arrow(_p0, p, _stroke); break; }
            case Line l: l.X2 = p.X; l.Y2 = p.Y; break;
            case Polyline pl: pl.Points.Add(p); break;
            case Rectangle r: { double x = Math.Min(_p0.X, p.X), y = Math.Min(_p0.Y, p.Y); Canvas.SetLeft(r, x); Canvas.SetTop(r, y); r.Width = Math.Abs(p.X - _p0.X); r.Height = Math.Abs(p.Y - _p0.Y); break; }
            case Ellipse el: { double x = Math.Min(_p0.X, p.X), y = Math.Min(_p0.Y, p.Y); Canvas.SetLeft(el, x); Canvas.SetTop(el, y); el.Width = Math.Abs(p.X - _p0.X); el.Height = Math.Abs(p.Y - _p0.Y); break; }
        }
    }
    void Up(object s, MouseButtonEventArgs e)
    {
        if (!_drawing) return; _drawing = false; _canvas.ReleaseMouseCapture();
        var live = _live; _live = null; _pen = null; if (live == null) return;
        if (live is Rectangle r && (_tool is "pixelate" or "spotlight" or "crop"))
        {
            _canvas.Children.Remove(live);
            var rect = new Int32Rect((int)Canvas.GetLeft(r), (int)Canvas.GetTop(r), (int)r.Width, (int)r.Height);
            rect.X = Math.Clamp(rect.X, 0, _img.PixelWidth - 1); rect.Y = Math.Clamp(rect.Y, 0, _img.PixelHeight - 1); rect.Width = Math.Min(rect.Width, _img.PixelWidth - rect.X); rect.Height = Math.Min(rect.Height, _img.PixelHeight - rect.Y);
            if (rect.Width < 4 || rect.Height < 4) return;
            if (_tool == "crop") { Crop(rect); return; }
            if (_tool == "pixelate")
            {
                var part = new CroppedBitmap(Flatten(), rect); int k = Math.Max(2, Math.Min(rect.Width, rect.Height) / 10);
                var small = new TransformedBitmap(part, new ScaleTransform(1.0 / k, 1.0 / k)); small.Freeze();
                var rr = new Rectangle { Width = rect.Width, Height = rect.Height, Fill = new ImageBrush(small) { Stretch = Stretch.Fill } };
                RenderOptions.SetBitmapScalingMode(rr, BitmapScalingMode.NearestNeighbor); Canvas.SetLeft(rr, rect.X); Canvas.SetTop(rr, rect.Y); Add(rr); return;
            }
            // spotlight: dim everything outside the box
            var dim = new System.Windows.Shapes.Path { Fill = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, _img.PixelWidth, _img.PixelHeight)), new RectangleGeometry(new Rect(rect.X, rect.Y, rect.Width, rect.Height))) };
            Add(dim); return;
        }
        _undo.Add(live);
    }
    // The picture with the marks so far, as one bitmap.
    BitmapSource Flatten()
    {
        var rtb = new RenderTargetBitmap(_img.PixelWidth, _img.PixelHeight, 96, 96, PixelFormats.Pbgra32); rtb.Render(_stage); rtb.Freeze(); return rtb;
    }
    void Crop(Int32Rect r)
    {
        var flat = Flatten(); var cropped = new CroppedBitmap(flat, r); cropped.Freeze();
        _img = cropped; _canvas.Children.Clear(); _undo.Clear(); _view.Source = _img; _canvas.Width = _stage.Width = _img.PixelWidth; _canvas.Height = _stage.Height = _img.PixelHeight;
    }
    // What gets copied or saved: the picture, its marks, then the background and watermark if chosen.
    BitmapSource Render()
    {
        var core = Flatten(); var s = Settings.Current;
        if (s.ShotBackdrop == "none" && s.ShotWatermark.Length == 0) return core;
        double pad = s.ShotBackdrop == "none" ? 0 : 56; double w = core.PixelWidth + pad * 2, h = core.PixelHeight + pad * 2;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            if (s.ShotBackdrop != "none")
            {
                var (c1, c2) = s.ShotBackdrop switch { "sunset" => ("#FF7E5F", "#FEB47B"), "ocean" => ("#2193B0", "#6DD5ED"), "graphite" => ("#232526", "#414345"), _ => ("#D7DDE8", "#F4F7FA") };
                dc.DrawRectangle(new LinearGradientBrush((Color)ColorConverter.ConvertFromString(c1), (Color)ColorConverter.ConvertFromString(c2), 45), null, new Rect(0, 0, w, h));
                dc.PushOpacity(0.35); dc.DrawRoundedRectangle(Brushes.Black, null, new Rect(pad - 2, pad + 8, core.PixelWidth + 4, core.PixelHeight + 4), 16, 16); dc.Pop();
                dc.PushClip(new RectangleGeometry(new Rect(pad, pad, core.PixelWidth, core.PixelHeight), 14, 14));
            }
            dc.DrawImage(core, new Rect(pad, pad, core.PixelWidth, core.PixelHeight));
            if (s.ShotBackdrop != "none") dc.Pop();
            if (s.ShotWatermark.Length > 0)
            {
                var ft = new FormattedText(s.ShotWatermark, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 16, new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)), 1.0);
                dc.DrawText(ft, new Point(w - ft.Width - pad - 10, h - ft.Height - pad - 8));
            }
        }
        var rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32); rtb.Render(dv); rtb.Freeze(); return rtb;
    }
}

// A picture that stays on top of everything until you close it.
public class ShotPin : Window
{
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    static readonly List<ShotPin> All = new(); bool _through;
    public ShotPin(BitmapSource img)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; Title = "Perch pin";
        double k = Math.Min(1, Math.Min(900.0 / img.PixelWidth, 700.0 / img.PixelHeight)); Width = img.PixelWidth * k + 12; Height = img.PixelHeight * k + 12;
        var wa = SystemParameters.WorkArea; Left = wa.Right - Width - 40 - All.Count * 24; Top = wa.Top + 60 + All.Count * 24;
        var im = new Image { Source = img, Stretch = Stretch.Uniform };
        Content = new Border { Margin = new Thickness(6), CornerRadius = new CornerRadius(6), BorderBrush = B("#66FFFFFF"), BorderThickness = new Thickness(1), Child = im, ClipToBounds = true, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.5 } };
        MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) Close(); else DragMove(); };
        MouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control) Opacity = Math.Clamp(Opacity + e.Delta / 1200.0, 0.2, 1);
            else { double f = e.Delta > 0 ? 1.08 : 0.92; double nw = Math.Clamp(Width * f, 80, 3000), nh = Height * nw / Width; Left += (Width - nw) / 2; Top += (Height - nh) / 2; Width = nw; Height = nh; }
        };
        var menu = new ContextMenu();
        void Item(string t, Action a) { var mi = new MenuItem { Header = t }; mi.Click += (_, _) => a(); menu.Items.Add(mi); }
        Item("Copy", () => Shot.Copy(img)); Item("Save…", () => { var f = Shot.NewFile(); Shot.Save(img, f); Svc.Notices.Add("Saved", Path.GetFileName(f), ""); });
        Item("Ignore clicks (Ctrl+Alt+click to undo)", () => { _through = true; var h = new System.Windows.Interop.WindowInteropHelper(this).Handle; SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x20); Opacity = Math.Min(Opacity, 0.85); });
        Item("Close", Close); Item("Close all pins", () => { foreach (var p in All.ToList()) p.Close(); });
        ContextMenu = menu; All.Add(this); Closed += (_, _) => All.Remove(this);
    }
}
