using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Perch;

// ───────────────────────── Settings ─────────────────────────
public class Site { public string Name { get; set; } = ""; public string Url { get; set; } = ""; public string Color { get; set; } = "#3A3A3C"; }

public partial class Settings
{
    public bool RunAtStartup { get; set; }
    public string OpenOn { get; set; } = "last";          // last | music
    public string LastTab { get; set; } = "music";
    public string Shape { get; set; } = "island";         // island | notch
    public double TopDistance { get; set; } = 6;
    public string Position { get; set; } = "center";      // left | center | right | free (dragged)
    public double FreeX { get; set; }                      // centre of the island, when dragged
    public double FreeY { get; set; }                      // its top edge, when dragged
    public bool LockPosition { get; set; }
    public double EdgeMargin { get; set; } = 24;
    public bool ReduceMotion { get; set; }
    public string Resting { get; set; } = "none";       // none | clock | stats
    public bool MirrorNotifications { get; set; } = true;
    public bool HideInFullscreen { get; set; } = true;
    public bool DayClock { get; set; }
    public double UiScale { get; set; } = 1;
    public bool HighContrast { get; set; }
    public string MotionMode { get; set; } = "windows";     // windows | on | off
    public string ContrastMode { get; set; } = "windows";
    public string SizeMode { get; set; } = "windows";
    public string Theme { get; set; } = "glass";             // glass | candy | graphite | black
    public int ClockHue { get; set; } = -1;                  // desktop clock colour: -1 is white
    public List<Reminder> Reminders { get; set; } = new();
    public bool ReminderPopups { get; set; } = true;
    [System.Text.Json.Serialization.JsonIgnore] public bool NoMotion => ReduceMotion || MotionMode == "on" || (MotionMode == "windows" && !System.Windows.SystemParameters.ClientAreaAnimation);
    [System.Text.Json.Serialization.JsonIgnore] public bool ContrastOn => HighContrast || ContrastMode == "on" || (ContrastMode == "windows" && System.Windows.SystemParameters.HighContrast);
    // Windows' own "Text size" setting (Settings, Accessibility) gives a percentage; the island follows it up to 150%.
    [System.Text.Json.Serialization.JsonIgnore] public double Scale
    {
        get
        {
            if (SizeMode != "windows") return Math.Clamp(UiScale, 1, 1.5);
            try { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility"); if (k?.GetValue("TextScaleFactor") is int f) return Math.Clamp(f / 100.0, 1, 1.5); } catch { }
            return 1;
        }
    }
    public bool Glass { get; set; } = true;
    public string GlassTint { get; set; } = "clear"; // clear | tinted
    public double GlassClarity { get; set; } = 0.53; // 0 = fully frosted, 1 = almost no blur
    public double GlassHue { get; set; } = 212;            // 0..360, the colour of the glass
    public double GlassColorStrength { get; set; }         // 0 = neutral glass, 1 = strongly coloured
    public bool GlassAdaptColor { get; set; }              // take the colour from what is behind instead of the hue
    public string Fullscreen { get; set; } = "thin";       // thin | hide | stay, while a fullscreen app is in front
    public bool VolumeDisplay { get; set; } = true;
    public bool FocusFollowsMouse { get; set; }
    public List<string> ClipIgnoredApps { get; set; } = new();

    // ── mouse and keyboard tweaks (all off until switched on) ──
    public bool ScrollInvert { get; set; }                  // flip the mouse wheel's vertical direction; touchpads are left alone
    public bool ScrollInvertH { get; set; }                 // flip a tilt wheel's direction
    public bool ScrollSideways { get; set; }                // the wheel scrolls sideways while a chosen key is held
    public string ScrollSidewaysKey { get; set; } = "alt";  // alt | shift | rctrl
    public double ScrollSpeed { get; set; } = 100;          // percent of one notch
    public bool ScrollLinear { get; set; }                  // every notch scrolls the same, however fast the wheel spins
    public bool ScrollSmooth { get; set; }                  // a notch glides instead of jumping
    public bool NoMouseAccel { get; set; }                  // turn "enhance pointer precision" off
    public string MouseAccelPrev { get; set; } = "";        // the value to bring back when that is turned off again
    public string BtnBack { get; set; } = "default";       // what the Back side button does
    public string BtnForward { get; set; } = "default";
    public string BtnMiddle { get; set; } = "default";
    public bool ClickFilter { get; set; }                   // ignore a second click that comes too soon (a worn button)
    public int ClickFilterMs { get; set; } = 40;
    public bool KeyDebounce { get; set; }                   // ignore a repeated letter that comes too soon (a worn key)
    public int KeyDebounceMs { get; set; } = 45;
    public List<string> InputIgnoredApps { get; set; } = new(); // programs the mouse and snippet tweaks never touch
    public bool InputPauseInGames { get; set; } = true;     // the mouse tweaks step aside while a fullscreen app is in front
    public string SuperKey { get; set; } = "off";           // off | caps: Caps Lock held becomes Ctrl+Alt+Shift
    public string SuperTap { get; set; } = "none";          // what a quick tap of that key does: none | escape | commandbar | island | caps
    public bool QuitGuard { get; set; }                     // make Alt+F4 / Ctrl+W need a hold or a second press
    public string QuitGuardMode { get; set; } = "hold";     // hold | double
    public string QuitGuardKeys { get; set; } = "both";     // altf4 | ctrlw | both
    public int QuitGuardHoldMs { get; set; } = 900;
    public List<string> QuitGuardApps { get; set; } = new(); // empty means every program
    public bool SnippetExpand { get; set; }                 // typing a trigger replaces it with its text
    public List<TextSnippet> TextSnippets { get; set; } = new();
    public string CleaningStyle { get; set; } = "black";    // black | indicator
    public int CleaningMaxSeconds { get; set; } = 300;      // a safety net: cleaning mode always ends by itself

    // ── system helpers ──
    public List<string> AutoQuitApps { get; set; } = new();     // quit these when their last window closes
    public bool AwakeWhileApps { get; set; }
    public List<string> AwakeApps { get; set; } = new();        // stay awake while any of these is running
    public bool AwakeOnCharger { get; set; }                    // stay awake while plugged in
    public bool AudioPriorityOn { get; set; }
    public List<string> AudioPriority { get; set; } = new();    // output device ids, best first
    public int HeadphoneDropVolume { get; set; } = -1;          // after headphones go away, speakers are held to this level; -1 is off
    public bool BluetoothSleep { get; set; }                    // switch Bluetooth off during sleep and back on at wake
    public List<string> RadialItems { get; set; } = new() { "screenshot", "playpause", "mute", "colour", "lock", "desktop" };
    public bool ClipPersist { get; set; }                       // keep clipboard history between sessions (encrypted for this account)
    public int ClipMaxEntries { get; set; } = 100;
    public int ClipKeepHours { get; set; }                      // 0 keeps entries until you clear them
    public string AiKey { get; set; } = "";                 // the API key, encrypted for this Windows account
    public string AiModel { get; set; } = "claude-sonnet-5-5";
    public double Corners { get; set; } = 1;                // 0 square .. 1 fully round
    public string Accent { get; set; } = "#FFFFFF";         // colour of the bars, progress and levels; "windows" follows the system accent
    public bool VisualizerBars { get; set; }                // false: square-ended pixels, true: rounded bars
    public string MusicBackground { get; set; } = "album";  // album | plain
    public int OpenDelayMs { get; set; } = 120;             // how long the pointer rests before the island opens
    public string AnimSpeed { get; set; } = "normal";       // relaxed | normal | quick
    public string MotionRate { get; set; } = "balanced";    // fluid | balanced | saver (frame-rate cap)
    public bool ReverseScroll { get; set; }
    public bool HiddenUntilHover { get; set; }
    public int MonitorIndex { get; set; } = -1;             // -1 is the main display
    public bool KeepNoticesLonger { get; set; }
    public bool CapsHud { get; set; } = true;
    public bool BrightnessHud { get; set; } = true;
    public bool BatteryTimeLeft { get; set; } = true;
    public bool SongTitleInPill { get; set; }
    public bool CapturesOnShelf { get; set; } = true;
    public string TimeFormat { get; set; } = "system";      // system | 12 | 24
    public string SpeedUnit { get; set; } = "MBs";          // MBs | Mbps
    public string ReminderSound { get; set; } = "until";    // until | once | off
    public bool GamerEverywhere { get; set; }
    public bool GamerCpu { get; set; } = true;
    public bool GamerGpu { get; set; } = true;
    public bool GamerRam { get; set; } = true;
    public bool GamerDown { get; set; } = true;
    public bool GamerUp { get; set; }
    public bool GamerClock { get; set; }
    public string GamerBackground { get; set; } = "black";  // black | glass | none
    public List<AppRule> AppRules { get; set; } = new();
    public bool CallTimer { get; set; } = true;            // the pill counts up while an app is using the microphone
    public bool PowerPopups { get; set; } = true;          // plugging in or unplugging
    public bool OutputPopups { get; set; } = true;         // the sound output changed (headphones in or out)
    public bool HoverOpensInGames { get; set; }            // false: over a fullscreen app Perch ignores the pointer and opens only from its hotkey
    public bool SlimUnderMaximized { get; set; } = true;   // a thin line while a maximised window is in front
    public bool GamerOverlay { get; set; }                 // over fullscreen apps the thin bar shows CPU, GPU, RAM and network
    public bool MicReadout { get; set; } = true;           // a readout whenever the microphone is muted or unmuted
    public bool HideFromCapture { get; set; }              // keep Perch out of screenshots, recordings and screen sharing
    public int ClipAutoClearMinutes { get; set; }          // 0 = never clear the system clipboard
    public bool CleanLinks { get; set; }                   // strip tracking tags from copied links automatically
    public bool SystemAlerts { get; set; } = true;
    public bool DownloadNotices { get; set; } = true;
    public List<string> Snippets { get; set; } = new();     // saved pieces of text, one click to copy
    public double ClockX { get; set; } = -1;
    public double ClockY { get; set; } = -1;
    public double ClockScale { get; set; } = 1;
    public Dictionary<string, string> Hotkeys { get; set; } = new() { ["toggle"] = "Ctrl+Alt+I" };
    // What an action uses when the user has never set it. A binding the user cleared stays cleared.
    public static readonly Dictionary<string, string> HotkeyDefaults = new() { ["toggle"] = "Ctrl+Alt+I", ["commandbar"] = "Ctrl+Alt+K", ["radial"] = "Ctrl+Alt+R" };
    public string Hotkey(string id) => Hotkeys.TryGetValue(id, out var c) ? c : HotkeyDefaults.GetValueOrDefault(id, "");
    public double NoticeSeconds { get; set; } = 5;
    public bool NotifyEndpoint { get; set; } = true;
    public int Port { get; set; } = 47824;
    public string City { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    public bool Fahrenheit { get; set; }
    public int FocusMinutes { get; set; } = 25;
    public List<string> HiddenTabs { get; set; } = new();
    public List<Site> Sites { get; set; } = new();
    public List<string> Slots { get; set; } = new() { "", "", "", "", "", "" };
    public List<string> Pinned { get; set; } = new();

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Perch");
    static string FilePath => Path.Combine(Dir, "settings.json");
    public static Settings Current { get; private set; } = Load();

    static Settings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { }
        return new();
    }
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

public class TextSnippet
{
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public bool InLibrary { get; set; } = true;
    public string Trigger { get; set; } = "";
    public string Text { get; set; } = "";
}

public class AppRule
{
    public string Exe { get; set; } = "";
    public string Mode { get; set; } = "strip"; // hide | strip | thin | stay
}

public class Reminder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTime At { get; set; }
    public string Repeat { get; set; } = "none"; // none | daily | weekly
    public bool Fired { get; set; }              // it has gone off and is waiting for Snooze or Done
}

// ───────────────────────── Spring ─────────────────────────
// Damping-ratio / response spring: interruptible, always continues from the live value and velocity.
public class Spring
{
    public double Value, Target, Velocity;
    public double Response = 0.34, Damping = 0.82;
    public Spring(double v) { Value = Target = v; }
    public bool Settled => Math.Abs(Value - Target) < 0.05 && Math.Abs(Velocity) < 0.5;
    public void Snap() { Value = Target; Velocity = 0; }
    public void Step(double dt)
    {
        double w = 2 * Math.PI / Response, k = w * w, c = 2 * Damping * w;
        // sub-step for stability on long frames
        int n = Math.Max(1, (int)Math.Ceiling(dt / 0.004));
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            double a = -k * (Value - Target) - c * Velocity;
            Velocity += a * h;
            Value += Velocity * h;
        }
        if (Settled) Snap();
    }
}

// ───────────────────────── UI helpers ─────────────────────────
public static class UI
{
    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Display = new("Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static string Dim => Settings.Current.ContrastOn ? "#D8D8DC" : "#8E8E93";
    // The accent colour for bars, progress and levels. "windows" follows the colour chosen in Windows' own settings.
    public static string Accent
    {
        get
        {
            var a = Settings.Current.Accent;
            if (a != "windows") return a.StartsWith('#') ? a : "#FFFFFF";
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                if (k?.GetValue("AccentColor") is int v) return $"#{v & 0xFF:X2}{(v >> 8) & 0xFF:X2}{(v >> 16) & 0xFF:X2}"; // stored as ABGR
            }
            catch { }
            return "#0A84FF";
        }
    }
    // Every clock in Perch follows one setting: Windows' own format, or 12 or 24 hours.
    public static string TimeText(DateTime t) => Settings.Current.TimeFormat == "12" ? t.ToString("h:mm tt") : Settings.Current.TimeFormat == "24" ? t.ToString("HH:mm") : t.ToString("t");
    public const string Card = "#1FFFFFFF", CardHi = "#2EFFFFFF", Line = "#1AFFFFFF";
    public static readonly CubicEase EaseOut = new() { EasingMode = EasingMode.EaseOut };

    public static SolidColorBrush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public static TextBlock T(string s, double size = 12, string color = "#FFFFFF", bool bold = false, bool mono = false)
        => new()
        {
            Text = Loc.Tr(s), FontSize = size, Foreground = B(color), FontFamily = mono ? Mono : (size >= 20 ? Display : Font),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

    public static TextBlock Ico(string glyph, double size = 14, string color = "#FFFFFF")
        => new()
        {
            Text = glyph, FontFamily = Icons, FontSize = size, Foreground = B(color),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
        };

    public static Border Box(UIElement? child, string bg = Card, double radius = 12, Thickness? pad = null)
    {
        var b = new Border { Child = child, Background = B(bg), CornerRadius = new CornerRadius(radius), Padding = pad ?? new Thickness(10) };
        if (bg == Card || bg == CardHi) Rimmed(b);
        return b;
    }
    // A one-pixel lit edge, taken out of the padding so nothing inside moves.
    static void Rimmed(Border b)
    {
        var p = b.Padding;
        if (p.Left < 1 || p.Top < 1 || p.Right < 1 || p.Bottom < 1) return;
        b.BorderBrush = Glass.CardRim; b.BorderThickness = new Thickness(1);
        b.Padding = new Thickness(p.Left - 1, p.Top - 1, p.Right - 1, p.Bottom - 1);
    }

    // A pressable surface: reacts on pointer-down (scale 0.97), commits on release inside.
    public static Border Btn(UIElement content, Action onClick, string bg = Card, double radius = 10, Thickness? pad = null, string? hover = null)
    {
        var st = new ScaleTransform(1, 1);
        var normal = B(bg);
        var live = new SolidColorBrush(normal.Color); // unfrozen, so hover can glide between colours
        var hi = B(hover ?? (bg == "#FFFFFF" ? "#E5E5EA" : bg == "#00FFFFFF" ? "#14FFFFFF" : CardHi));
        var b = new Border
        {
            Child = content, Background = live, CornerRadius = new CornerRadius(radius), Padding = pad ?? new Thickness(10, 6, 10, 6),
            RenderTransform = st, RenderTransformOrigin = new Point(0.5, 0.5), Cursor = Cursors.Hand
        };
        void Scale(double to, int ms)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = EaseOut };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
        if (bg == Card || bg == CardHi) Rimmed(b);
        void Tone(SolidColorBrush to, int ms)
        {
            // a caller that painted its own background keeps the plain swap
            if (!ReferenceEquals(b.Background, live)) { b.Background = to; return; }
            if (Settings.Current.NoMotion) { live.BeginAnimation(SolidColorBrush.ColorProperty, null); live.Color = to.Color; return; }
            live.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to.Color, TimeSpan.FromMilliseconds(ms)) { EasingFunction = EaseOut });
        }
        b.MouseEnter += (_, _) => Tone(hi, 110);
        b.MouseLeave += (_, _) => { Tone(normal, 220); Scale(1, 140); };
        b.MouseLeftButtonDown += (_, e) => { Scale(0.96, 90); e.Handled = true; };
        b.MouseLeftButtonUp += (_, e) => { Scale(1, 140); if (b.IsMouseOver) onClick(); e.Handled = true; };
        return b;
    }

    public static Border IconBtn(string glyph, Action onClick, double size = 14, string color = "#FFFFFF", double box = 28)
    {
        var b = Btn(Ico(glyph, size, color), onClick, "#00FFFFFF", box / 2, new Thickness(0));
        b.Width = b.Height = box;
        return b;
    }

    public static Border Pill(string text, Action onClick, bool primary = false, string? glyph = null)
    {
        var fg = primary ? "#000000" : "#FFFFFF";
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (glyph != null) { var i = Ico(glyph, 12, fg); i.Margin = new Thickness(0, 0, 6, 0); sp.Children.Add(i); }
        sp.Children.Add(T(text, 11.5, fg, true));
        return Btn(sp, onClick, primary ? "#FFFFFF" : Card, 9, new Thickness(12, 6, 12, 6));
    }

    public static StackPanel Row(params UIElement[] kids)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var k in kids) sp.Children.Add(k);
        return sp;
    }
    public static StackPanel Col(params UIElement[] kids)
    {
        var sp = new StackPanel();
        foreach (var k in kids) sp.Children.Add(k);
        return sp;
    }
    public static T M<T>(this T e, double l, double t = 0, double r = 0, double b = 0) where T : FrameworkElement { e.Margin = new Thickness(l, t, r, b); return e; }
    public static T At<T>(this T e, int row, int col, int rowSpan = 1, int colSpan = 1) where T : UIElement
    {
        Grid.SetRow(e, row); Grid.SetColumn(e, col); Grid.SetRowSpan(e, rowSpan); Grid.SetColumnSpan(e, colSpan);
        return e;
    }
    public static Grid MakeGrid(string cols, string rows)
    {
        var g = new Grid();
        foreach (var c in cols.Split(',', StringSplitOptions.RemoveEmptyEntries)) g.ColumnDefinitions.Add(new ColumnDefinition { Width = Len(c) });
        foreach (var r in rows.Split(',', StringSplitOptions.RemoveEmptyEntries)) g.RowDefinitions.Add(new RowDefinition { Height = Len(r) });
        return g;
    }
    static GridLength Len(string s)
    {
        s = s.Trim();
        if (s == "auto") return GridLength.Auto;
        if (s.EndsWith('*')) return new GridLength(s.Length == 1 ? 1 : double.Parse(s[..^1]), GridUnitType.Star);
        return new GridLength(double.Parse(s));
    }

    public static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
}

// ───────────────────────── Slider ─────────────────────────
public class MiniSlider : Grid
{
    readonly Border _fill = new(), _track = new();
    readonly bool _vertical;
    double _v;
    public bool Dragging { get; private set; }
    public event Action<double>? Changed;
    public event Action<double>? Committed;

    public MiniSlider(double thickness = 4, bool vertical = false, string? fill = null, string track = "#33FFFFFF", double radius = 2)
    {
        _vertical = vertical;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        _track.Background = UI.B(track); _track.CornerRadius = new CornerRadius(radius);
        _fill.Background = UI.B(fill ?? UI.Accent);
        if (vertical)
        {
            _track.Width = _fill.Width = thickness; Width = thickness;
            _fill.VerticalAlignment = VerticalAlignment.Bottom; _fill.CornerRadius = new CornerRadius(0);
            _track.ClipToBounds = true;
        }
        else
        {
            _track.Height = _fill.Height = thickness; MinHeight = Math.Max(thickness, 16);
            _fill.HorizontalAlignment = HorizontalAlignment.Left; _fill.CornerRadius = new CornerRadius(radius);
        }
        Children.Add(_track); Children.Add(_fill);
        if (vertical) SizeChanged += (_, _) => Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), radius, radius);
        SizeChanged += (_, _) => Lay();
        MouseLeftButtonDown += (_, e) => { CaptureMouse(); Dragging = true; Set(e); e.Handled = true; };
        MouseMove += (_, e) => { if (Dragging) Set(e); };
        MouseLeftButtonUp += (_, e) => { if (!Dragging) return; Dragging = false; ReleaseMouseCapture(); Committed?.Invoke(_v); e.Handled = true; };
        MouseWheel += (_, e) => { Value = _v + ((e.Delta > 0) != Settings.Current.ReverseScroll ? 0.04 : -0.04); Changed?.Invoke(_v); Committed?.Invoke(_v); e.Handled = true; };
    }
    // A track that is itself the scale (a hue strip, say) shows a marker instead of a fill.
    Border? _knob;
    public void UseTrack(Brush track, double height = 8)
    {
        _track.Background = track; _track.Height = height; _track.CornerRadius = new CornerRadius(height / 2);
        _fill.Visibility = Visibility.Collapsed;
        _knob = new Border
        {
            Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left,
            BorderBrush = UI.B("#55000000"), BorderThickness = new Thickness(1), IsHitTestVisible = false
        };
        Children.Add(_knob);
        Lay();
    }
    public double Value
    {
        get => _v;
        set { _v = Math.Clamp(double.IsNaN(value) ? 0 : value, 0, 1); Lay(); }
    }
    void Set(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        Value = _vertical ? 1 - p.Y / Math.Max(1, ActualHeight) : p.X / Math.Max(1, ActualWidth);
        Changed?.Invoke(_v);
    }
    void Lay()
    {
        if (_vertical) _fill.Height = Math.Max(0, ActualHeight * _v);
        else _fill.Width = Math.Max(0, ActualWidth * _v);
        if (_knob != null) _knob.Margin = new Thickness(Math.Clamp(ActualWidth * _v - 7, 0, Math.Max(0, ActualWidth - 14)), 0, 0, 0);
    }
}

// ───────────────────────── Toggle ─────────────────────────
public class Toggle : Border
{
    readonly Border _knob = new() { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left };
    bool _on;
    public event Action<bool>? Changed;
    public Toggle(bool on, Action<bool>? changed = null)
    {
        Width = 40; Height = 22; CornerRadius = new CornerRadius(11); Cursor = Cursors.Hand; Child = _knob;
        VerticalAlignment = VerticalAlignment.Center;
        if (changed != null) Changed += changed;
        _on = on; Paint(false);
        MouseLeftButtonUp += (_, e) => { _on = !_on; Paint(true); Changed?.Invoke(_on); e.Handled = true; };
    }
    public bool IsOn { get => _on; set { _on = value; Paint(false); } }
    void Paint(bool animate)
    {
        Background = UI.B(_on ? "#FFFFFF" : "#3A3A3C");
        _knob.Background = UI.B(_on ? "#111113" : "#AEAEB2");
        var to = new Thickness(_on ? 21 : 3, 0, 0, 0);
        if (animate && !Settings.Current.NoMotion)
            _knob.BeginAnimation(MarginProperty, new ThicknessAnimation(to, TimeSpan.FromMilliseconds(160)) { EasingFunction = UI.EaseOut });
        else { _knob.BeginAnimation(MarginProperty, null); _knob.Margin = to; }
    }
}

// ───────────────────────── Segmented control ─────────────────────────
public class Segmented : Border
{
    readonly StackPanel _sp = new() { Orientation = Orientation.Horizontal };
    readonly string[] _opts; int _sel; readonly Action<int> _cb; readonly bool _stretch;
    public Segmented(string[] opts, int selected, Action<int> changed, bool stretch = false)
    {
        _opts = opts; _sel = selected; _cb = changed; _stretch = stretch;
        CornerRadius = new CornerRadius(10); Padding = new Thickness(3); Background = UI.B("#14FFFFFF");
        BorderBrush = UI.B(UI.Line); BorderThickness = new Thickness(1);
        HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        Build();
    }
    public int Selected { get => _sel; set { _sel = value; Build(); } }
    void Build()
    {
        if (_stretch)
        {
            var g = new Grid();
            for (int i = 0; i < _opts.Length; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.Children.Add(Item(i).At(0, i));
            }
            Child = g;
            return;
        }
        _sp.Children.Clear();
        for (int i = 0; i < _opts.Length; i++) _sp.Children.Add(Item(i));
        Child = _sp;
    }
    Border Item(int i)
    {
        bool on = i == _sel;
        var t = UI.T(_opts[i], 11.5, on ? "#000000" : "#D1D1D6", true);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return UI.Btn(t, () => { if (_sel == i) return; _sel = i; Build(); _cb(i); }, on ? "#FFFFFF" : "#00FFFFFF", 7, new Thickness(12, 5, 12, 5));
    }
}
