using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace Perch;

// The glass material: brushes for the tinted surface, its bright rim and its sheen,
// plus the blurred layer that sits behind a window and shows the desktop through it.
public static class Glass
{
    // Off when the user asks for solid surfaces or brighter text: legibility wins over material.
    // Black and Graphite are solid themes: no glass at all. Glass and Candy are the two glass looks.
    public static string Theme => Settings.Current.Theme;
    public static bool Solid_ => Theme == "black" || Theme == "pureblack" || Theme == "graphite";
    // Soft themes are glass with a colour wash of their own.
    public static bool IsSoft => Theme is "candy" or "rose" or "lavender" or "peach" or "sakura";
    static (Color A, Color B) SoftPair() => Theme switch
    {
        "rose" => (Color.FromRgb(0x8A, 0x35, 0x50), Color.FromRgb(0x4A, 0x2A, 0x48)),
        "lavender" => (Color.FromRgb(0x5B, 0x46, 0x90), Color.FromRgb(0x2E, 0x3A, 0x78)),
        "peach" => (Color.FromRgb(0x9A, 0x5A, 0x3C), Color.FromRgb(0x7A, 0x35, 0x50)),
        "sakura" => (Color.FromRgb(0x9A, 0x58, 0x78), Color.FromRgb(0x5A, 0x3C, 0x68)),
        _ => (Color.FromRgb(0x7A, 0x3A, 0x6E), Color.FromRgb(0x2C, 0x3E, 0x7E)),
    };
    public static bool On => Settings.Current.Glass && !Solid_ && !Settings.Current.ContrastOn && !Unavailable && !Suspended;
    public static bool Suspended; // over a fullscreen app the island is a plain solid sliver: no blur, no screen reads
    public static bool Unavailable; // set if this Windows cannot give us a blurred backdrop

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static LinearGradientBrush Vertical(params (string Hex, double At)[] stops)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var s in stops) g.GradientStops.Add(new GradientStop(C(s.Hex), s.At));
        g.Freeze();
        return g;
    }

    static LinearGradientBrush Diagonal(params (string Hex, double At)[] stops)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        foreach (var s in stops) g.GradientStops.Add(new GradientStop(C(s.Hex), s.At));
        g.Freeze();
        return g;
    }
    // Clear glass lets the backdrop through; tinted glass keeps a darker veil for busy or bright backgrounds.
    public static bool Clear => Settings.Current.GlassTint != "tinted";

    // The edge of a glass slab: light enters at the top-left corner and leaves at the bottom-right.
    public static readonly Brush Rim = Diagonal(("#D9FFFFFF", 0), ("#4DFFFFFF", 0.22), ("#1FFFFFFF", 0.6), ("#80FFFFFF", 1));
    public static readonly Brush RimSolid = UI.B("#1FFFFFFF");
    // The same idea, quieter, for cards that sit on the glass.
    public static readonly Brush CardRim = Diagonal(("#52FFFFFF", 0), ("#17FFFFFF", 0.3), ("#0DFFFFFF", 0.65), ("#2EFFFFFF", 1));
    // A soft highlight across the top of a surface, and the fainter light gathered along its bottom edge.
    public static readonly Brush Sheen = Vertical(("#30FFFFFF", 0), ("#12FFFFFF", 0.4), ("#00FFFFFF", 1));
    public static readonly Brush Foot = Vertical(("#00FFFFFF", 0), ("#14FFFFFF", 1));
    // Blurred inward from the edge, this reads as the thickness of the glass.
    public static readonly Brush Glow = UI.B("#59FFFFFF");

    // How much darker the island's glass must be for white text to stay readable: 0 over a dark
    // backdrop, 1 over a bright one. The island measures the screen around itself and sets this.
    public static double Veil;
    // Tinted glass keeps its darker veil at every setting, but the see-through slider thins it
    // enough for the sharp view behind to register.
    static double TintedShade => 1 - 0.5 * Math.Clamp(Settings.Current.GlassClarity, 0, 1);
    static double Shade => Clear ? Veil : TintedShade;
    // ── colour ──
    // The glass can carry a colour: one the user picks by hue, or one read from the screen behind it.
    public static Color Backdrop = Color.FromRgb(40, 40, 46), PanelBackdrop = Color.FromRgb(40, 40, 46);
    public static double Strength => Math.Clamp(Settings.Current.GlassColorStrength, 0, 1);
    public static bool Coloured => Strength > 0.01;
    public static Color FromHue(double hue, double sat = 0.85, double val = 1)
    {
        hue = ((hue % 360) + 360) % 360; double c = val * sat, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = val - c;
        (double r, double g, double b) = hue < 60 ? (c, x, 0d) : hue < 120 ? (x, c, 0d) : hue < 180 ? (0d, c, x) : hue < 240 ? (0d, x, c) : hue < 300 ? (x, 0d, c) : (c, 0d, x);
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
    // The colour as it goes into the glass: an adapted colour is pushed toward full saturation so a
    // grey desktop still gives a recognisable cast, and any colour is held dark enough for white text.
    static Color Ink(Color source)
    {
        double r = source.R / 255.0, g = source.G / 255.0, b = source.B / 255.0;
        if (Settings.Current.GlassAdaptColor)
        {
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (max > 0.02)
            {
                double grey = (r + g + b) / 3, boost = 1.6; // widen the spread around grey
                r = Math.Clamp(grey + (r - grey) * boost, 0, 1); g = Math.Clamp(grey + (g - grey) * boost, 0, 1); b = Math.Clamp(grey + (b - grey) * boost, 0, 1);
                double lift = 0.55 / Math.Max(0.08, Math.Max(r, Math.Max(g, b))); // bring dim scenes up to a visible level
                if (lift > 1) { lift = Math.Min(lift, 3); r = Math.Min(1, r * lift); g = Math.Min(1, g * lift); b = Math.Min(1, b * lift); }
            }
        }
        double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b, cap = 0.24;
        if (lum > cap) { double k = cap / lum; r *= k; g *= k; b *= k; }
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
    public static Color Hue => FromHue(Settings.Current.GlassHue);
    static Color Source(bool panel) => Settings.Current.GlassAdaptColor ? (panel ? PanelBackdrop : Backdrop) : Hue;
    static Color Dye(Color neutral, bool panel)
    {
        if (!Coloured) return neutral;
        var ink = Ink(Source(panel)); double s = Strength * 0.9;
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * s);
        // colour needs some body to show on clear glass, so strength also thickens it a little
        byte a = (byte)Math.Min(255, neutral.A + (255 - neutral.A) * Strength * 0.35);
        return Color.FromArgb(a, L(neutral.R, ink.R), L(neutral.G, ink.G), L(neutral.B, ink.B));
    }

    static Color Mix(string clear, string heavy, double shade)
    {
        Color a = C(clear), b = C(heavy); double t = Math.Clamp(shade, 0, 1);
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }
    static Brush Blend((string Clear, string Heavy) top, (string Clear, string Heavy) bottom, double? shade = null, bool panel = false)
    {
        double t = shade ?? Shade;
        // the veil is worked out first and the colour laid into it, so the veil's darkness is kept
        var g = new LinearGradientBrush(Dye(Mix(top.Clear, top.Heavy, t), panel), Dye(Mix(bottom.Clear, bottom.Heavy, t), panel), 90);
        g.Freeze();
        return g;
    }

    public static Brush Surface(bool open)
        => !On ? (Coloured && !Suspended && !Settings.Current.ContrastOn && !Solid_ ? Solid(open) : UI.B(Theme == "pureblack" ? "#000000" : Theme == "black" ? (open ? "#121214" : "#0B0B0D") : Theme == "graphite" ? (open ? "#2C2C30" : "#232326") : (open ? "#161618" : "#0E0E10")))
        : IsSoft ? Candy(open, false)
        : open ? Blend(("#4D34343C", "#D61E1E22"), ("#6B141418", "#E00E0E10"))
        : Blend(("#57303036", "#D61C1C20"), ("#75101014", "#E00C0C0E"));

    // Solid surfaces take the colour too, as an opaque dark shade of it.
    static Brush Solid(bool open)
    {
        var d = Dye(C(open ? "#FF161618" : "#FF0E0E10"), false);
        var b = new SolidColorBrush(Color.FromRgb(d.R, d.G, d.B)); b.Freeze();
        return b;
    }
    // Candy glass: a soft pink-to-blue wash, light enough that what is behind still shows.
    static Brush Candy(bool open, bool panel)
    {
        byte a = (byte)(panel ? 0xB0 : open ? 0x8C : 0x99);
        var (c1, c2) = SoftPair();
        var g = new LinearGradientBrush(Color.FromArgb(a, c1.R, c1.G, c1.B), Color.FromArgb(a, c2.R, c2.G, c2.B), 35);
        g.Freeze();
        return g;
    }
    public static Brush Tinted(Color tint)
    {
        if (IsSoft && On) return Candy(false, false);
        byte edge = !On ? (byte)0xFF : (byte)(0x66 + (0xD6 - 0x66) * Shade), mid = !On ? (byte)0xFF : (byte)(0x73 + (0xCC - 0x73) * Shade);
        // album colours can be pale; under a bright backdrop pull them down so white text still reads
        double keep = On ? 1 - 0.5 * Shade : 1;
        tint = Color.FromRgb((byte)(tint.R * keep), (byte)(tint.G * keep), (byte)(tint.B * keep));
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(edge, 0x12, 0x12, 0x14), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(mid, tint.R, tint.G, tint.B), 0.55));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(edge, 0x12, 0x12, 0x14), 1));
        g.Freeze();
        return g;
    }

    // A window full of small text keeps a heavier veil than the island does.
    public static double PanelVeil; // the settings window's own reading of the screen around it
    public static Brush Panel
    {
        get
        {
            if (!On) return UI.B(Theme == "pureblack" ? "#000000" : Theme == "black" ? "#0E0E10" : Theme == "graphite" ? "#26262A" : "#161618");
            if (IsSoft) return Candy(true, true);
            return Blend(("#8F26262C", "#E01C1C20"), ("#A8121214", "#EB101012"), Clear ? PanelVeil : TintedShade, true);
        }
    }
}

// A click-through window kept directly behind its host. It holds one composition visual that
// shows Windows' own blurred view of whatever is underneath, clipped to a smooth rounded shape.
public class GlassBackdrop
{
    readonly Window _host;
    HwndSource? _src; IntPtr _hwnd, _hostHwnd;
    Compositor? _comp; DesktopWindowTarget? _target; SpriteVisual? _visual; CompositionRoundedRectangleGeometry? _clip;
    static object? _queue;
    public bool IsVisible { get; private set; }
    public IntPtr Handle => _hwnd;
    // How much of the blur is laid over the real view in clear mode. Below 1, what is behind shows
    // through sharp as well as blurred, which is what makes the glass read as see-through.
    // Reach is how far the see-through setting may thin the blur on this surface.
    public float Reach = 0.85f;
    float Frost => 1f - Reach * (float)Math.Clamp(Settings.Current.GlassClarity, 0, 1) * (Settings.Current.Theme == "acrylic" ? 0.35f : 1f); // acrylic is a frostier glass

    public GlassBackdrop(Window host)
    {
        _host = host;
        try
        {
            if (_queue == null)
            {
                var opt = new QueueOptions { Size = Marshal.SizeOf<QueueOptions>(), ThreadType = 2, ApartmentType = 2 };
                CreateDispatcherQueueController(opt, out var q); _queue = q;
            }
            // no redirection surface: the window shows nothing but the composition visual
            var p = new HwndSourceParameters("Perch glass")
            {
                WindowStyle = unchecked((int)0x80000000),
                ExtendedWindowStyle = 0x00200000 | 0x80 | 0x08000000 | 0x20 | (host.Topmost ? 0x8 : 0),
                PositionX = -200, PositionY = -200, Width = 10, Height = 10
            };
            _src = new HwndSource(p); _hwnd = _src.Handle;
            int on = 1; DwmSetWindowAttribute(_hwnd, 17, ref on, 4); // let this window sample what is behind it
            _comp = new Compositor();
            _comp.As<ICompositorDesktopInterop>().CreateDesktopWindowTarget(_hwnd, false, out var raw);
            _target = DesktopWindowTarget.FromAbi(raw); Marshal.Release(raw);
            _visual = _comp.CreateSpriteVisual();
            _visual.Brush = _comp.CreateHostBackdropBrush();
            _clip = _comp.CreateRoundedRectangleGeometry(); _visual.Clip = _comp.CreateGeometricClip(_clip);
            _target.Root = _visual;
        }
        catch { Glass.Unavailable = true; _visual = null; }
        host.LocationChanged += (_, _) => Follow();
        host.SizeChanged += (_, _) => Follow();
        host.Activated += (_, _) => Follow();
        host.IsVisibleChanged += (_, _) => Follow();
        host.Closed += (_, _) => { _target?.Dispose(); _src?.Dispose(); _src = null; _hwnd = IntPtr.Zero; };
    }

    // Shape of the glass in the host's own device-independent units; square top for the notch style.
    // The shape, in physical pixels measured from the host window's corner.
    float _sx, _sy, _sw, _sh; bool _shaped;

    public void SetShape(double x, double y, double w, double h, double radius, bool squareTop = false)
    {
        if (_visual == null || _clip == null) return;
        _visual.Opacity = Frost;
        float k = (float)VisualTreeHelper.GetDpi(_host).DpiScaleX;
        // half a pixel inside the drawn edge, so the rim line always covers the seam
        float fw = Math.Max(1, (float)w * k - 1), fh = Math.Max(1, (float)h * k - 1), r = Math.Max(0, (float)radius * k - 0.5f);
        _sx = (float)x * k + 0.5f; _sy = (float)y * k + 0.5f; _sw = fw; _sh = fh; _shaped = true;
        // the window is cut to whole pixels around the shape; the visual keeps the leftover fraction
        _visual.Offset = new System.Numerics.Vector3(_sx - MathF.Floor(_sx) + Pad, _sy - MathF.Floor(_sy) + Pad, 0);
        _visual.Size = new System.Numerics.Vector2(fw, fh);
        // a square top is the same rounded shape pushed up past the edge of the visual
        float lift = squareTop ? r + 2 : 0;
        _clip.Offset = new System.Numerics.Vector2(0, -lift);
        _clip.Size = new System.Numerics.Vector2(fw, fh + lift);
        _clip.CornerRadius = new System.Numerics.Vector2(r, r);
        if (IsVisible) Place(false);
    }

    // The glass window covers only the shape itself (plus a hair of slack while it is resizing).
    // A window that is not layered takes the pointer wherever it is, so it must never be larger
    // than the surface drawn on top of it, or it would swallow clicks meant for what is underneath.
    const int Pad = 2;
    void Place(bool show)
    {
        if (!_shaped) return;
        if (_hostHwnd == IntPtr.Zero) _hostHwnd = new WindowInteropHelper(_host).Handle;
        if (_hostHwnd == IntPtr.Zero || !GetWindowRect(_hostHwnd, out var r)) return;
        int x = r.L + (int)MathF.Floor(_sx) - Pad, y = r.T + (int)MathF.Floor(_sy) - Pad;
        int w = (int)MathF.Ceiling(_sw) + 1 + Pad * 2, h = (int)MathF.Ceiling(_sh) + 1 + Pad * 2;
        Native.SetWindowPos(_hwnd, _hostHwnd, x, y, w, h, 0x0010 | 0x0200 | (show ? 0x0040u : 0u)); // never activate
    }

    // Stay directly behind the host, and hide with it.
    public void Follow()
    {
        if (_hwnd == IntPtr.Zero) return;
        bool want = Glass.On && _host.IsVisible && _host.Opacity > 0 && _host.WindowState != WindowState.Minimized;
        if (!want) { if (IsVisible) { Native.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0080); IsVisible = false; } return; }
        Place(true);
        IsVisible = true;
    }

    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICompositorDesktopInterop { void CreateDesktopWindowTarget(IntPtr hwnd, bool topmost, out IntPtr target); }
    [StructLayout(LayoutKind.Sequential)] struct QueueOptions { public int Size, ThreadType, ApartmentType; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [DllImport("CoreMessaging.dll")] static extern int CreateDispatcherQueueController(QueueOptions options, [MarshalAs(UnmanagedType.IUnknown)] out object controller);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
