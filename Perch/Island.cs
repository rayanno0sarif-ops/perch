using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static Perch.UI;

namespace Perch;

public class IslandWindow : Window
{
    public static IslandWindow Instance { get; private set; } = null!;
    const double EW = 396, CW = 214, CH = 40, ToastW = 330, WinW = 460, WinH = 320;

    readonly Border _island = new();
    readonly Grid _collapsed = new();
    readonly Grid _expanded = new();
    readonly StackPanel _tabBar = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    readonly ContentControl _host = new();
    readonly Spring _w = new(CW), _h = new(CH), _r = new(CH / 2);
    readonly Stopwatch _clock = new();
    readonly DispatcherTimer _close = new() { Interval = TimeSpan.FromMilliseconds(320) };
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _toastTimer = new();
    List<TabBase> _tabs = TabBase.All();
    TabBase? _active;
    bool _open, _animating, _dragInside, _pinnedOpen; int _hold;
    Notice? _toast;
    // volume display
    const double VolW = 236, ThinW = 58, ThinH = 5, GamerW = 318, GamerH = 20;
    readonly TextBlock _gamerText = T("", 10.5, "#FFFFFF", true, true);
    bool _gamer;
    readonly Grid _volView = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly TextBlock _volIcon = Ico("\uE767", 14), _volPct = T("", 11.5, "#FFFFFF", true, true);
    readonly Border _volFill = new() { Height = 5, CornerRadius = new CornerRadius(2.5), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
    readonly Border _volTrack = new() { Height = 5, CornerRadius = new CornerRadius(2.5), Background = B("#33FFFFFF") };
    readonly DispatcherTimer _volPoll = new() { Interval = TimeSpan.FromMilliseconds(180) }, _volHide = new() { Interval = TimeSpan.FromMilliseconds(1700) };
    readonly DispatcherTimer _dwell = new() { Interval = TimeSpan.FromMilliseconds(220) };
    double _gamerW = 318; int _lastKeys = -1; bool _hidden, _vanished; DateTime _batteryAt; bool _onBattery;
    readonly DispatcherTimer _hoverWatch = new() { Interval = TimeSpan.FromMilliseconds(90) };
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetKeyState(int key);
    (string Glyph, string Text, string Color)? _flash; DateTime? _callStart; string? _lastOutput; bool? _lastOnline; int _devTick;
    bool _fsThin; // thin because a fullscreen app (probably a game) is in front, as opposed to a maximised window
    double _volSeen = -1; bool _muteSeen, _volShown, _thin, _micMode, _hotHidden; bool? _micSeen;
    readonly TextBlock _micLabel = T("", 12, "#FFFFFF", true); Grid _volBar = null!;
    public readonly Dictionary<string, string> HotkeyState = new();
    public static readonly (string Id, string Label)[] HotkeyActions =
    {
        ("toggle", "Open or close the island"), ("play", "Play or pause"),
        ("timer", "Start or pause the focus timer"), ("mic", "Mute or unmute the microphone"),
        ("output", "Switch to the next sound output"), ("colour", "Pick a colour from the screen"), ("hide", "Hide or show Perch"),
        ("commandbar", "Open the command bar"), ("snapleft", "Snap the window to the left half"), ("snapright", "Snap the window to the right half"),
        ("snaptop", "Snap the window to the top half"), ("snapbottom", "Snap the window to the bottom half"), ("snapmax", "Maximise the window"), ("snapcenter", "Centre the window"),
        ("powermode", "Cycle the Windows power mode"), ("copycolour", "Copy the colour under the cursor"), ("screenshot", "Screenshot of every monitor, to Pictures and the shelf"),
        ("record", "Start or stop recording the screen"), ("newreminder", "New reminder"),
        ("pasteplain", "Paste as plain text"), ("slot1", "Open launcher slot 1"), ("slot2", "Open launcher slot 2"), ("slot3", "Open launcher slot 3"), ("slot4", "Open launcher slot 4"), ("slot5", "Open launcher slot 5"), ("slot6", "Open launcher slot 6"),
        ("cleaning", "Cleaning mode"), ("radial", "Radial menu"),
        ("snaptl", "Snap to the top left corner"), ("snaptr", "Snap to the top right corner"), ("snapbl", "Snap to the bottom left corner"), ("snapbr", "Snap to the bottom right corner"),
        ("thirdl", "Snap to the left third"), ("thirdc", "Snap to the middle third"), ("thirdr", "Snap to the right third"),
        ("twothirdsl", "Snap to the left two thirds"), ("twothirdsr", "Snap to the right two thirds"),
        ("nextdisplay", "Move the window to the next display"), ("restore", "Put the window back where it was"),
        ("centerhalf", "Snap: Centre half"), ("twothirdsc", "Snap: Centre two thirds"), ("vthird1", "Snap: Top third"), ("vthird2", "Snap: Middle row third"), ("vthird3", "Snap: Bottom third"), ("vtwothirdst", "Snap: Top two thirds"), ("vtwothirdsb", "Snap: Bottom two thirds"), ("rowq1", "Snap: Top quarter"), ("rowq2", "Snap: Upper middle quarter"), ("rowq3", "Snap: Lower middle quarter"), ("rowq4", "Snap: Bottom quarter"), ("colq1", "Snap: Left quarter"), ("colq2", "Snap: Left middle quarter"), ("colq3", "Snap: Right middle quarter"), ("colq4", "Snap: Right quarter"), ("tl6", "Snap: Top left sixth"), ("tc6", "Snap: Top centre sixth"), ("tr6", "Snap: Top right sixth"), ("bl6", "Snap: Bottom left sixth"), ("bc6", "Snap: Bottom centre sixth"), ("br6", "Snap: Bottom right sixth"), ("marginmax", "Snap: Maximise with margin"), ("fullscreen", "Snap: Cover the whole display"), ("prevdisplay", "Snap: Previous display"),
        ("switcher", "Open the window switcher"), ("brightup", "Raise the brightness of the display under the pointer"), ("brightdown", "Lower the brightness of the display under the pointer"), ("shotarea", "Screenshot of an area or window"), ("shotwindow", "Screenshot of a window"), ("shotfull", "Screenshot of the whole display"), ("recscreen", "Record the screen"), ("camera", "Open the camera mirror"), ("snippets", "Open the snippet menu"), ("clipwindow", "Open the clipboard history window"), ("quitothers", "Close every other program"), ("pointerdisplay", "Move the pointer to the next display")
    };
    IntPtr _hwnd; bool _fsHidden;
    GlassBackdrop? _glass;
    readonly Border _sheen = new() { Height = 46, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Background = Glass.Sheen };
    readonly Border _foot = new() { Height = 26, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Background = Glass.Foot };
    readonly Border _glow = new() { BorderBrush = Glass.Glow, BorderThickness = new Thickness(1.5), IsHitTestVisible = false, Margin = new Thickness(-1), Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 6 } };
    readonly ScaleTransform _exScale = new(1, 1);
    readonly TranslateTransform _hostShift = new();

    // collapsed parts
    readonly Image _cArt = new() { Stretch = Stretch.UniformToFill };
    readonly TextBlock _cIcon = Ico("", 12, "#8E8E93");
    readonly TextBlock _cText = T("", 11.5, "#FFFFFF", true);
    readonly StackPanel _bars = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Border _cArtBox = new() { Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Background = B("#26FFFFFF") };

    public IslandWindow()
    {
        Instance = this;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        Width = WinW; Height = WinH; Title = "Perch";
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        BuildCollapsed();
        BuildExpanded();
        var inner = new Grid();
        inner.Children.Add(_sheen); inner.Children.Add(_foot); inner.Children.Add(_glow); inner.Children.Add(_expanded); inner.Children.Add(_collapsed); inner.Children.Add(_volView);
        _gamerText.HorizontalAlignment = HorizontalAlignment.Center; _gamerText.IsHitTestVisible = false; _gamerText.Opacity = 0; inner.Children.Add(_gamerText);
        _island.Child = inner;
        _island.VerticalAlignment = VerticalAlignment.Top;
        _island.Background = Glass.Surface(false);
        _island.BorderBrush = Glass.On ? Glass.Rim : Glass.RimSolid; _island.BorderThickness = new Thickness(1);
        _island.AllowDrop = true;
        var root = new Grid(); root.Children.Add(_island);
        Content = root;

        _island.MouseEnter += (_, _) =>
        {
            _close.Stop();
            if (_fsThin && !_open && !Settings.Current.HoverOpensInGames) return;
            if (_open) return;
            int delay = Settings.Current.OpenDelayMs; if (_thin) delay = Math.Max(220, delay); // over a browser or game it waits a little longer
            if (delay <= 0) Open(); else { _dwell.Interval = TimeSpan.FromMilliseconds(delay); _dwell.Start(); }
        };
        // the wheel over the row of tab icons turns the tabs; "reverse scrolling" flips it
        _island.PreviewMouseWheel += (_, e) =>
        {
            if (!_open || _active == null || e.GetPosition(_island).Y > 36) return;
            var vis = Visible().ToList(); int i = vis.IndexOf(_active); if (i < 0) return;
            bool forward = (e.Delta < 0) != Settings.Current.ReverseScroll;
            Select(vis[Math.Clamp(i + (forward ? 1 : -1), 0, vis.Count - 1)].Id); e.Handled = true;
        };
        _hoverWatch.Tick += (_, _) => { WatchHidden(); FocusFollows(); };
        _hoverWatch.Start();
        _dwell.Tick += (_, _) => { _dwell.Stop(); if (_island.IsMouseOver) Open(); };
        BuildVolume();
        _island.PreviewMouseLeftButtonDown += DragDown;
        _island.PreviewMouseMove += DragMove_;
        _island.PreviewMouseLeftButtonUp += DragUp;
        // if something takes the pointer away mid-drag, the button is still down: its release must not click
        _island.LostMouseCapture += (_, _) => { if (_dragging) { EndDrag(); _swallowUp = Mouse.LeftButton == MouseButtonState.Pressed; } };
        _veilTimer.Tick += (_, _) => { StepVeil(); if (Math.Abs(Glass.Veil - _veilTarget) < 0.02) _veilTimer.Stop(); };
        _island.MouseLeave += (_, _) => _close.Start();
        _island.DragEnter += (_, e) => { _dragInside = true; _close.Stop(); if (e.Data.GetDataPresent(DataFormats.FileDrop)) { Open(); Select("shelf"); } };
        _island.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        _island.DragLeave += (_, _) => { _dragInside = false; _close.Start(); };
        _island.Drop += (_, e) =>
        {
            _dragInside = false;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) { Svc.AddToShelf(files); Select("shelf"); }
            _close.Start();
        };
        _close.Tick += (_, _) =>
        {
            if (_island.IsMouseOver || _dragInside || _hold > 0 || _pinnedOpen || Mouse.Captured != null) return;
            if (IsActive && Keyboard.FocusedElement is TextBox) return;
            _close.Stop(); Close_();
        };
        Deactivated += (_, _) => { if (_hold == 0 && !_island.IsMouseOver) { _pinnedOpen = false; Close_(); } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { _pinnedOpen = false; Close_(); } };

        _tick.Tick += (_, _) => OnTick();
        _tick.Start();
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); _toast = null; RefreshChrome(); };

        Svc.Media.Changed += RefreshChrome;
        Svc.Focus.Changed += RefreshChrome;
        Svc.Notices.Added += n =>
        {
            _toast = n;
            _toastTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, Settings.Current.NoticeSeconds) * (Settings.Current.KeepNoticesLonger ? 2 : 1));
            _toastTimer.Stop(); _toastTimer.Start();
            RefreshChrome();
        };

        ApplySettings();
        var start = Settings.Current.OpenOn == "music" ? "music" : Settings.Current.LastTab;
        _active = Visible().FirstOrDefault(t => t.Id == start) ?? Visible().FirstOrDefault();
        BuildTabBar();
        RefreshChrome();
        SourceInitialized += OnSource;
    }

    // ───────── native hooks: tool window, hotkey, clipboard ─────────
    void OnSource(object? s, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.SetWindowLong(hwnd, -20, Native.GetWindowLong(hwnd, -20) | 0x80); // WS_EX_TOOLWINDOW: no Alt+Tab entry
        HwndSource.FromHwnd(hwnd)!.AddHook(WndProc);
        Native.AddClipboardFormatListener(hwnd);
        // Ctrl+Alt+Space
        _hwnd = hwnd;
        RegisterHotkeys();
        _glass = new GlassBackdrop(this);
        ApplyGlass();
    }
    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == 0x0312)
        {
            int id = wp.ToInt32() - 1;
            if (id >= 0 && id < HotkeyActions.Length)
            {
                switch (HotkeyActions[id].Id)
                {
                    case "toggle": TogglePinned(); break;
                    case "play": Svc.Media.Toggle(); break;
                    case "timer": Svc.Focus.Toggle(); break;
                    case "output": Quick.NextOutput(); break;
                    case "colour": Tools.PickColour(); break;
                    case "commandbar": CommandBar.Toggle(); break;
                    case "powermode": { int cur = Native.ActivePlan(); Native.SetPlan((Math.Max(0, cur) + 1) % 3); Svc.Notices.Add("Power mode changed", "", "\uE945"); break; }
                    case "copycolour": Tools.CopyColourUnderCursor(); break;
                    case "pasteplain": Tools.PastePlain(); break;
                    case "screenshot": Tools.ScreenshotToShelf(); break;
                    case "record": Rec.Toggle(); break;
                    case "newreminder": RemindersTab.WantFocus = true; ShowReminders(); _remindOpen = false; break;
                    case "slot1": case "slot2": case "slot3": case "slot4": case "slot5": case "slot6":
                        {
                            int n = HotkeyActions[id].Id[4] - '1'; var slots = Settings.Current.Slots;
                            if (n < slots.Count && slots[n].Length > 0 && System.IO.File.Exists(slots[n])) Svc.Open(slots[n]);
                            break;
                        }
                    case "snapleft": Snap.To("left"); break;
                    case "snapright": Snap.To("right"); break;
                    case "snaptop": Snap.To("top"); break;
                    case "snapbottom": Snap.To("bottom"); break;
                    case "snapmax": Snap.To("max"); break;
                    case "snapcenter": Snap.To("center"); break;
                    case "snaptl": Snap.To("tl"); break;
                    case "snaptr": Snap.To("tr"); break;
                    case "snapbl": Snap.To("bl"); break;
                    case "snapbr": Snap.To("br"); break;
                    case "thirdl": Snap.To("thirdl"); break;
                    case "thirdc": Snap.To("thirdc"); break;
                    case "thirdr": Snap.To("thirdr"); break;
                    case "twothirdsl": Snap.To("twothirdsl"); break;
                    case "twothirdsr": Snap.To("twothirdsr"); break;
                    case "nextdisplay": Snap.To("nextdisplay"); break;
                    case "restore": Snap.To("restore"); break;
                    case "switcher": Switcher.Toggle(); break;
                    case "brightup": Displays.Nudge(1); break;
                    case "brightdown": Displays.Nudge(-1); break;
                    case "shotarea": Shot.Start("area"); break;
                    case "shotwindow": Shot.Start("window"); break;
                    case "shotfull": Shot.Start("full"); break;
                    case "recscreen": Rec.Toggle(); break;
                    case "camera": CameraWindow.Toggle(); break;
                    case "snippets": QuickPicks.Snippets(); break;
                    case "clipwindow": QuickPicks.Clipboard_(); break;
                    case "quitothers": Tools2.QuitOthers(false); break;
                    case "pointerdisplay": Windows2.PointerToNextDisplay(); break;
                    case "centerhalf": case "twothirdsc": case "vthird1": case "vthird2": case "vthird3": case "vtwothirdst": case "vtwothirdsb":
                    case "rowq1": case "rowq2": case "rowq3": case "rowq4": case "colq1": case "colq2": case "colq3": case "colq4":
                    case "tl6": case "tc6": case "tr6": case "bl6": case "bc6": case "br6": case "marginmax": case "fullscreen": case "prevdisplay":
                        Snap.To(HotkeyActions[id].Id); break;
                    case "cleaning": CleaningMode.Start(); break;
                    case "radial": Radial.Toggle(); break;
                    case "hide":
                        _hotHidden = !_hotHidden;
                        if (_hotHidden) { Close_(); Hide(); } else Show();
                        break;
                    case "mic":
                        Svc.Mic.Muted = !Svc.Mic.Muted;
                        Svc.Notices.Add(Svc.Mic.Muted ? "Microphone muted" : "Microphone on", "", Svc.Mic.Muted ? "\uEC54" : "\uE720");
                        break;
                }
                handled = true;
            }
        }
        else if (msg == 0x031D) { Svc.Clip.Capture(); Watch.ClipboardChanged(); }
        return IntPtr.Zero;
    }
    public void RegisterHotkeys()
    {
        if (_hwnd == IntPtr.Zero) return;
        HotkeyState.Clear();
        for (int i = 0; i < HotkeyActions.Length; i++)
        {
            Native.UnregisterHotKey(_hwnd, i + 1);
            var combo = Settings.Current.Hotkey(HotkeyActions[i].Id);
            HotkeyState[HotkeyActions[i].Id] =
                combo.Length == 0 ? "Not set"
                : !ParseHotkey(combo, out var mods, out var vk) ? "Not a usable combination"
                : Native.RegisterHotKey(_hwnd, i + 1, mods | 0x4000, vk) ? "Active" : "Taken by another app";
        }
    }
    public static bool ParseHotkey(string s, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        foreach (var part in s.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part)
            {
                case "Ctrl": mods |= 2; break;
                case "Alt": mods |= 1; break;
                case "Shift": mods |= 4; break;
                case "Win": mods |= 8; break;
                default:
                    if (!Enum.TryParse<Key>(part, out var k)) return false;
                    vk = (uint)KeyInterop.VirtualKeyFromKey(k);
                    break;
            }
        }
        return vk != 0 && mods != 0;
    }
    string RestingText()
    {
        switch (Settings.Current.Resting)
        {
            case "clock": return UI.TimeText(DateTime.Now);
            case "stats": return $"CPU {Svc.Stats.Cpu * 100:0}%   RAM {Svc.Stats.Ram * 100:0}%";
            default: return "";
        }
    }
    // A reminder that is due opens the card on its tab and keeps it open until it has been dealt with.
    bool _remindOpen;
    public void ShowReminders()
    {
        if (!Visible().Any(t => t.Id == "reminders")) return;
        if (_hotHidden) { _hotHidden = false; Show(); }
        if (_fsHidden) { _fsHidden = false; Show(); }
        _remindOpen = true; _pinnedOpen = true;
        Select("reminders");
        Open(true);
    }
    public void ReleaseReminders()
    {
        if (!_remindOpen) return;
        _remindOpen = false; _pinnedOpen = false;
        if (!_island.IsMouseOver) Close_();
    }
    public void TogglePinned()
    {
        if (_hotHidden) { _hotHidden = false; Show(); }
        if (_fsHidden) { _fsHidden = false; Show(); } // asked for by hotkey or tray: come back even over a fullscreen app
        if (_open) { _pinnedOpen = false; Close_(); }
        else { _pinnedOpen = true; Open(true); } // keyboard-initiated: no animation
    }

    // ───────── pop-ups: power, sound output, calls ─────────
    // A short message on the closed pill, in the same slot as the volume and microphone readouts.
    public void Flash(string glyph, string text, string color = "#FFFFFF")
    {
        if (_open || _fsHidden || _hotHidden) return;
        _flash = (glyph, text, color); _micMode = true;
        bool was = _volShown; _volShown = true;
        _volHide.Stop(); _volHide.Start();
        PaintVolume(false);
        if (!was) { RefreshChrome(); Retarget(); }
    }
    double _briSeen = -1; bool _briBusy;
    async System.Threading.Tasks.Task CheckBrightness()
    {
        if (_briBusy) return; _briBusy = true;
        try
        {
            var b = await Brightness.Get();
            if (b != null)
            {
                if (_briSeen >= 0 && Math.Abs(b.Value - _briSeen) > 0.02 && !_open) Flash("\uE706", $"Brightness  {b.Value * 100:0}%");
                _briSeen = b.Value;
            }
        }
        catch { }
        finally { _briBusy = false; }
    }
    void PollDevices()
    {
        var s = Settings.Current;
        try
        {
            var p = System.Windows.Forms.SystemInformation.PowerStatus;
            if (p.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)
            {
                bool online = p.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                if (_lastOnline != null && _lastOnline != online && s.PowerPopups && p.BatteryLifePercent <= 1)
                    Flash(online ? "\uE83E" : "\uE850", (online ? "Charging  " : "On battery  ") + $"{p.BatteryLifePercent * 100:0}%" + (s.BatteryTimeLeft && !online && p.BatteryLifeRemaining > 0 ? $"  \u00B7  {p.BatteryLifeRemaining / 3600}h {p.BatteryLifeRemaining % 3600 / 60}m left" : ""));
                _lastOnline = online;
            }
        }
        catch { }
        if (s.CapsHud)
        {
            int keys = (GetKeyState(0x14) & 1) | ((GetKeyState(0x90) & 1) << 1) | ((GetKeyState(0x91) & 1) << 2);
            if (_lastKeys >= 0 && keys != _lastKeys)
            {
                int diff = keys ^ _lastKeys; string[] names = { "Caps Lock", "Num Lock", "Scroll Lock" };
                for (int i = 0; i < 3; i++) if ((diff & (1 << i)) != 0) Flash("\uE765", names[i] + ((keys & (1 << i)) != 0 ? " on" : " off"));
            }
            _lastKeys = keys;
        }
        if (s.BrightnessHud && _devTick % 4 == 0) _ = CheckBrightness();
        if (++_devTick % 2 == 0)
        {
            string id = Mixer.DefaultOutputId();
            if (id.Length > 0)
            {
                if (_lastOutput != null && _lastOutput != id && s.OutputPopups) { var name = Svc.Speaker.Name; Flash("\uE7F5", name.Length > 30 ? name[..29] + "\u2026" : name); }
                _lastOutput = id;
            }
            bool inUse = s.CallTimer && Mixer.MicInUse();
            if (inUse && _callStart == null) { _callStart = DateTime.Now; RefreshChrome(); }
            else if (!inUse && _callStart != null) { _callStart = null; RefreshChrome(); }
        }
        if (_callStart != null) RefreshChrome(); // the count moves every second
    }

    // ───────── volume display ─────────
    // When the system volume or mute changes while the island is closed, the pill briefly becomes
    // a level meter: speaker, bar, percentage. Changes made from Perch's own sliders do not trigger it.
    void BuildVolume()
    {
        var bar = new Grid { VerticalAlignment = VerticalAlignment.Center }; bar.Children.Add(_volTrack); bar.Children.Add(_volFill);
        _volPct.Width = 38; _volPct.TextAlignment = TextAlignment.Right;
        _volView.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _volView.ColumnDefinitions.Add(new ColumnDefinition());
        _volView.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _volBar = bar; _micLabel.Visibility = Visibility.Collapsed;
        _volView.Children.Add(_volIcon.At(0, 0)); _volView.Children.Add(bar.M(11, 0, 4).At(0, 1)); _volView.Children.Add(_micLabel.M(11, 0, 4).At(0, 1)); _volView.Children.Add(_volPct.At(0, 2));
        _volView.Margin = new Thickness(15, 0, 14, 0); _volView.Height = CH - 2; _volView.VerticalAlignment = VerticalAlignment.Top;
        _volTrack.SizeChanged += (_, _) => PaintVolume(false);
        _volPoll.Tick += (_, _) => PollVolume();
        _volHide.Tick += (_, _) => { _volHide.Stop(); _volShown = false; RefreshChrome(); Retarget(); };
        _volPoll.Start();
    }
    void PollVolume()
    {
        double v = 0; bool mute = false, haveSpk = false, micMuted = false, haveMic = false;
        try { haveSpk = Svc.Speaker.Read(out v, out mute); } catch { }
        try { haveMic = Svc.Mic.Read(out _, out micMuted); } catch { }
        bool volChanged = false, micChanged = false;
        if (haveSpk) { volChanged = _volSeen >= 0 && (Math.Abs(v - _volSeen) > 0.004 || mute != _muteSeen); _volSeen = v; _muteSeen = mute; } // the first reading only sets the baseline
        if (haveMic) { micChanged = _micSeen != null && _micSeen != micMuted; _micSeen = micMuted; }
        if (_open || _fsHidden || _hotHidden) return; // open: the user is on Perch's own controls
        bool showVol = volChanged && Settings.Current.VolumeDisplay, showMic = micChanged && Settings.Current.MicReadout;
        if (!showVol && !showMic) return;
        _micMode = showMic && !showVol; _flash = null; // if both moved at once, the speaker wins
        bool was = _volShown; _volShown = true;
        _volHide.Stop(); _volHide.Start();
        PaintVolume(was && !_micMode);
        if (!was) { RefreshChrome(); Retarget(); }
    }
    void PaintVolume(bool glide)
    {
        _volBar.Visibility = _volPct.Visibility = _micMode ? Visibility.Collapsed : Visibility.Visible;
        _micLabel.Visibility = _micMode ? Visibility.Visible : Visibility.Collapsed;
        if (_micMode && _flash != null)
        {
            _volIcon.Text = _flash.Value.Glyph; _volIcon.Foreground = B(_flash.Value.Color);
            _micLabel.Text = _flash.Value.Text;
            return;
        }
        if (_micMode)
        {
            bool m = _micSeen == true;
            _volIcon.Text = m ? "\uEC54" : "\uE720"; _volIcon.Foreground = B(m ? "#FF6961" : "#FFFFFF");
            _micLabel.Text = m ? "Microphone muted" : "Microphone on";
            return;
        }
        _volIcon.Foreground = Brushes.White;
        double v = Math.Max(0, _volSeen); bool mute = _muteSeen || v < 0.005;
        _volIcon.Text = mute ? "\uE74F" : v < 0.34 ? "\uE993" : v < 0.67 ? "\uE994" : "\uE995";
        _volPct.Text = _muteSeen ? "Off" : $"{v * 100:0}%";
        _volFill.Opacity = _muteSeen ? 0.35 : 1;
        double to = Math.Max(0, _volTrack.ActualWidth * v);
        if (glide && !Settings.Current.NoMotion && _volTrack.ActualWidth > 0)
            _volFill.BeginAnimation(WidthProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(110)) { EasingFunction = EaseOut });
        else { _volFill.BeginAnimation(WidthProperty, null); _volFill.Width = to; }
    }

    // ───────── dragging ─────────
    // Grab the top strip (the pill, or the tab bar once open) and the island follows the pointer 1:1
    // from the point where it was grabbed. A few pixels of slack keep ordinary clicks as clicks.
    Point? _grab; bool _dragging, _swallowUp;
    const double DragStrip = 38, DragSlack = 6;
    double Scale => Settings.Current.Scale;

    // Keep the open panel on screen: the island grows from its centre, so its centre stays half a panel from each side.
    Point ClampFree(double cx, double y)
    {
        double half = EW * Scale / 2 + 4;
        double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
        double r = l + SystemParameters.VirtualScreenWidth, b = t + SystemParameters.VirtualScreenHeight;
        return new Point(Math.Clamp(cx, l + half, Math.Max(l + half, r - half)), Math.Clamp(y, t, Math.Max(t, b - 250 * Scale)));
    }
    Point PointerOnScreen(MouseEventArgs e)
    {
        var p = PointToScreen(e.GetPosition(this));
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Point(p.X / dpi.DpiScaleX, p.Y / dpi.DpiScaleY);
    }
    void DragDown(object s, MouseButtonEventArgs e)
    {
        _grab = null; _swallowUp = false;
        if (Settings.Current.LockPosition) return;
        if (e.GetPosition(_island).Y > DragStrip) return; // only the top strip is a handle; cards keep their own gestures
        _grab = e.GetPosition(this);
    }
    void DragMove_(object s, MouseEventArgs e)
    {
        if (_grab == null || e.LeftButton != MouseButtonState.Pressed) { _grab = null; return; }
        var now = e.GetPosition(this);
        if (!_dragging)
        {
            if (Math.Abs(now.X - _grab.Value.X) < DragSlack && Math.Abs(now.Y - _grab.Value.Y) < DragSlack) return;
            BeginDrag();
        }
        var at = PointerOnScreen(e);
        // window origin that keeps the grabbed point under the pointer, then held on screen
        var c = ClampFree(at.X - _grab.Value.X + Width / 2, at.Y - _grab.Value.Y);
        Left = c.X - Width / 2; Top = c.Y;
        e.Handled = true;
    }
    void BeginDrag()
    {
        var s = Settings.Current;
        if (s.Position != "free")
        {
            // re-anchor on the island's centre without moving anything that is on screen right now
            double w = _island.Width * Scale;
            double cx = Left + (s.Position == "left" ? w / 2 : s.Position == "right" ? Width - w / 2 : Width / 2);
            double top = Top + _island.Margin.Top;
            double shift = cx - Width / 2 - Left;
            if (_grab != null) _grab = new Point(_grab.Value.X - shift, _grab.Value.Y - _island.Margin.Top);
            s.Position = "free";
            _island.HorizontalAlignment = HorizontalAlignment.Center;
            _island.Margin = new Thickness(0);
            Left = cx - Width / 2; Top = top;
            Apply();
        }
        _dragging = true;
        _hold++; // stays open while it is being carried
        _island.CaptureMouse();
        _island.Cursor = Cursors.SizeAll;
    }
    void DragUp(object s, MouseButtonEventArgs e)
    {
        _grab = null;
        if (_swallowUp) { _swallowUp = false; e.Handled = true; return; }
        if (!_dragging) return;
        e.Handled = true; // the release that ends a drag is not a click on whatever is underneath
        EndDrag();
    }
    void EndDrag()
    {
        _dragging = false; _grab = null;
        _island.ReleaseMouseCapture();
        _island.Cursor = null;
        var st = Settings.Current;
        st.FreeX = Left + Width / 2; st.FreeY = Top; st.Save();
        Hold(false);
        MeasureBackdrop();
    }

    // ───────── layout / settings ─────────
    public void ApplySettings()
    {
        var s = Settings.Current;
        double sw = SystemParameters.PrimaryScreenWidth, ox = 0, oy = 0;
        try
        {
            var all = System.Windows.Forms.Screen.AllScreens;
            if (s.MonitorIndex >= 0 && s.MonitorIndex < all.Length)
            {
                double dpiK = VisualTreeHelper.GetDpi(this).DpiScaleX; var b = all[s.MonitorIndex].Bounds;
                sw = b.Width / dpiK; ox = b.X / dpiK; oy = b.Y / dpiK;
            }
        }
        catch { }
        bool notch = s.Shape == "notch";
        bool free = s.Position == "free";
        _island.Margin = new Thickness(0, free || notch ? 0 : s.TopDistance, 0, 0);
        _island.HorizontalAlignment = s.Position == "left" ? HorizontalAlignment.Left : s.Position == "right" ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        double k = s.Scale;
        Width = WinW * k; Height = WinH * k;
        _island.LayoutTransform = new ScaleTransform(k, k);
        Top = oy;
        Left = ox + (s.Position == "left" ? s.EdgeMargin : s.Position == "right" ? sw - Width - s.EdgeMargin : (sw - Width) / 2);
        if (free)
        {
            var at = ClampFree(s.FreeX, s.FreeY);
            Left = at.X - Width / 2; Top = at.Y;
        }
        BuildTabBar();
        if (_active != null && s.HiddenTabs.Contains(_active.Id)) Select(Visible().FirstOrDefault()?.Id ?? "music");
        Retarget(true);
        _glass?.Follow();
    }
    // Keeps the island (and its glass layer) out of screenshots, recordings and screen sharing when asked.
    public void ApplyCapture()
    {
        bool on = Settings.Current.HideFromCapture;
        Native.ExcludeFromCapture(_hwnd, on);
        if (_glass != null) Native.ExcludeFromCapture(_glass.Handle, on);
    }
    // Glass on: blurred layer behind, bright rim, sheen. Glass off: the solid surface, no extra window.
    public void ApplyGlass()
    {
        ApplyCapture();
        _island.BorderBrush = Glass.On ? Glass.Rim : Glass.RimSolid;
        _sheen.Visibility = _foot.Visibility = _glow.Visibility = Glass.On ? Visibility.Visible : Visibility.Collapsed;
        MeasureBackdrop(true);
        RefreshChrome();
        Apply();
        _glass?.Follow();
    }
    // Clear glass adapts: it measures the screen just outside its own edges (below and beside, where
    // nothing of ours is drawn) and darkens itself when that is bright enough to wash out white text.
    double _veilTarget;
    void MeasureBackdrop(bool snap = false)
    {
        bool wantColour = Settings.Current.GlassAdaptColor && Glass.Coloured && !Glass.Suspended && !Settings.Current.ContrastOn;
        if ((!Glass.On || !Glass.Clear) && !wantColour) return;
        if (!IsVisible || Opacity == 0) return;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX, k = Settings.Current.Scale;
        double w = _island.Width * k, h = _island.Height * k; if (double.IsNaN(w) || double.IsNaN(h)) return;
        var pos = Settings.Current.Position;
        double x = Left + (pos == "left" ? 0 : pos == "right" ? Width - w : (Width - w) / 2), y = Top + _island.Margin.Top;
        // when closed, also look at the area the open panel is about to cover
        double below = _open ? 10 : Math.Max(10, ((_active?.Height ?? 180) - CH) * k), wide = _open ? w : EW * k;
        // the panel grows from the same edge the island is anchored to
        double sx = pos == "left" ? x : pos == "right" ? x + w - wide : x - (wide - w) / 2;
        double l = Native.ScreenSample((int)(sx * dpi), (int)((y + h + 3) * dpi), (int)(wide * dpi), (int)(below * dpi), out var seen);
        if (l < 0) return;
        if (Settings.Current.GlassAdaptColor && Glass.Coloured)
        {
            var was = Glass.Backdrop; double mix = snap ? 1 : 0.35; // ease toward the new colour so it never flickers
            var now = Color.FromRgb((byte)(was.R + (seen.R - was.R) * mix), (byte)(was.G + (seen.G - was.G) * mix), (byte)(was.B + (seen.B - was.B) * mix));
            if (Math.Abs(now.R - was.R) + Math.Abs(now.G - was.G) + Math.Abs(now.B - was.B) > 3) { Glass.Backdrop = now; RefreshChrome(); }
        }
        _veilTarget = Math.Clamp((l - 0.22) / 0.4, 0, 1);
        if (snap) Glass.Veil = _veilTarget;
        StepVeil();
    }
    void StepVeil()
    {
        if (Math.Abs(Glass.Veil - _veilTarget) < 0.02) return;
        Glass.Veil += (_veilTarget - Glass.Veil) * 0.5;
        if (Math.Abs(Glass.Veil - _veilTarget) < 0.02) Glass.Veil = _veilTarget;
        RefreshChrome();
    }
    readonly DispatcherTimer _veilTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    // Colours are baked into each card when it is built, so a contrast change builds them again.
    public void RebuildTabs()
    {
        var id = _active?.Id;
        _tabs = TabBase.All();
        _host.Content = null;
        _active = Visible().FirstOrDefault(t => t.Id == id) ?? Visible().FirstOrDefault();
        BuildTabBar();
        if (_open) ShowActive(false);
        RefreshChrome();
    }
    IEnumerable<TabBase> Visible() => _tabs.Where(t => !Settings.Current.HiddenTabs.Contains(t.Id));

    void BuildCollapsed()
    {
        var artGrid = new Grid(); artGrid.Children.Add(_cIcon); artGrid.Children.Add(_cArt);
        _cArtBox.Child = artGrid;
        _cArtBox.Clip = new RectangleGeometry(new Rect(0, 0, 26, 26), 7, 7);
        for (int i = 0; i < 4; i++)
        {
            var st = new ScaleTransform(1, 0.3);
            _bars.Children.Add(new Border { Width = 2.5, Height = 14, CornerRadius = new CornerRadius(Settings.Current.VisualizerBars ? 1.25 : 0), Background = B(UI.Accent), Margin = new Thickness(1.5, 0, 1.5, 0), RenderTransform = st, RenderTransformOrigin = new Point(0.5, 0.5) });
        }
        _collapsed.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _collapsed.ColumnDefinitions.Add(new ColumnDefinition());
        _collapsed.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _collapsed.Children.Add(_cArtBox.At(0, 0)); _collapsed.Children.Add(_cText.M(9, 0, 9).At(0, 1)); _collapsed.Children.Add(_bars.At(0, 2));
        _collapsed.Margin = new Thickness(7, 0, 12, 0);
        _collapsed.Height = CH - 2; _collapsed.VerticalAlignment = VerticalAlignment.Top;
        _collapsed.IsHitTestVisible = false;
    }

    void BuildExpanded()
    {
        _expanded.Width = EW - 2; _expanded.HorizontalAlignment = HorizontalAlignment.Center; _expanded.VerticalAlignment = VerticalAlignment.Top;
        _expanded.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        _expanded.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var dot = Btn(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(4.5), Background = B("#FF5F57") }, () => { _pinnedOpen = false; Close_(); }, "#00FFFFFF", 10, new Thickness(6));
        dot.HorizontalAlignment = HorizontalAlignment.Left; dot.Margin = new Thickness(10, 0, 0, 0); dot.ToolTip = "Close";
        _tabBar.Margin = new Thickness(34, 0, 12, 0); // keeps the tabs clear of the close dot
        var bar = new Grid(); bar.Children.Add(_tabBar); bar.Children.Add(dot);
        _expanded.Children.Add(bar.At(0, 0));
        _expanded.Children.Add(_host.At(1, 0));
        _expanded.Opacity = 0; _expanded.IsHitTestVisible = false;
        _expanded.RenderTransform = _exScale; _expanded.RenderTransformOrigin = new Point(0.5, 0);
        _host.RenderTransform = _hostShift;
    }

    void BuildTabBar()
    {
        _tabBar.Children.Clear();
        foreach (var t in Visible())
        {
            var tab = t; bool on = tab == _active;
            var ico = Ico(t.Glyph, 13, on ? "#FFFFFF" : "#8E8E93");
            // same padding as the settings icon; the dot is laid over the bottom edge so it cannot push the icon up
            var dot = new Border { Width = 3, Height = 3, CornerRadius = new CornerRadius(1.5), Background = Brushes.White, Opacity = on ? 1 : 0, Margin = new Thickness(0, 0, 0, -5), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
            var cell = new Grid(); cell.Children.Add(ico); cell.Children.Add(dot);
            var b = Btn(cell, () => Select(tab.Id), "#00FFFFFF", 8, new Thickness(0, 5, 0, 5));
            b.Width = Math.Clamp(300.0 / Math.Max(1, Visible().Count()), 14, 24); b.ToolTip = t.Name; ToolTipService.SetInitialShowDelay(b, 500);
            _tabBar.Children.Add(b);
        }
        _tabBar.Children.Add(new Border { Width = 1, Height = 12, Background = B("#33FFFFFF"), Margin = new Thickness(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
        var gear = Btn(Ico("", 13, "#8E8E93"), () => SettingsWindow.Open(), "#00FFFFFF", 8, new Thickness(0, 5, 0, 5));
        gear.Width = 24; gear.ToolTip = "Settings";
        _tabBar.Children.Add(gear);
    }

    public void Select(string id)
    {
        var t = Visible().FirstOrDefault(x => x.Id == id);
        if (t == null) return;
        bool changed = t != _active;
        _active = t;
        if (changed) { Settings.Current.LastTab = id; Settings.Current.Save(); }
        BuildTabBar();
        if (_open) ShowActive(changed);
        RefreshChrome();
        Retarget();
    }
    void ShowActive(bool fade)
    {
        if (_active == null) return;
        var v = _active.View;
        v.Height = Math.Max(20, _active.Height - 36 - v.Margin.Top - v.Margin.Bottom);
        if (!ReferenceEquals(_host.Content, v))
        {
            _host.Content = v;
            if (fade && !Settings.Current.NoMotion)
            {
                // the new tab rises a few pixels into place as it fades in
                v.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
                _hostShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(7, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } });
            }
        }
        _active.Shown(); _active.Tick();
    }

    // ───────── open / close ─────────
    void Open(bool instant = false)
    {
        if (_open) return;
        _open = true;
        UpdateSuspend(); // the open card is glass again, even over a game
        MeasureBackdrop(true); // judged while the area the panel will cover is still in view
        ShowActive(false);
        _expanded.IsHitTestVisible = true;
        Retarget(instant);
    }
    void Close_()
    {
        if (!_open) return;
        _open = false;
        _expanded.IsHitTestVisible = false;
        Keyboard.ClearFocus();
        UpdateSuspend();
        Retarget();
    }
    // Only the closed sliver is a plain solid bar (no blur layer, no screen reads). An open card is glass,
    // and over a fullscreen app the island lets the pointer pass through to the game until it is opened.
    void UpdateSuspend()
    {
        bool suspend = _thin && !_open;
        bool through = _fsThin && !_open && !Settings.Current.HoverOpensInGames;
        if (_hwnd != IntPtr.Zero)
        {
            int ex = Native.GetWindowLong(_hwnd, -20), want = through ? ex | 0x20 : ex & ~0x20; // WS_EX_TRANSPARENT
            if (want != ex) Native.SetWindowLong(_hwnd, -20, want);
        }
        if (Glass.Suspended != suspend) { Glass.Suspended = suspend; ApplyGlass(); }
    }
    public void Hold(bool on) { _hold = Math.Max(0, _hold + (on ? 1 : -1)); if (_hold == 0) _close.Start(); }
    public void Vanish(bool on) { _vanished = on; ApplyOpacity(); }
    void ApplyOpacity() { Opacity = _vanished || _hidden ? 0 : 1; _glass?.Follow(); }
    IntPtr _ffTarget; DateTime _ffSince;
    // Focus follows the mouse: rest the pointer on a window for a moment and it comes forward.
    void FocusFollows()
    {
        if (!Settings.Current.FocusFollowsMouse || _open || _hwnd == IntPtr.Zero) { _ffTarget = IntPtr.Zero; return; }
        Native.GetCursorPos(out var pt);
        var h = Native.RootWindowAt(pt.X, pt.Y);
        if (h == IntPtr.Zero || h == _hwnd) { _ffTarget = IntPtr.Zero; return; }
        if (h != _ffTarget) { _ffTarget = h; _ffSince = DateTime.Now; return; }
        if ((DateTime.Now - _ffSince).TotalMilliseconds < 450) return;
        Native.RaiseIfOrdinary(h);
    }
    // "Hidden until hover": nothing is drawn until the pointer is where the island would be.
    void WatchHidden()
    {
        if (_hwnd == IntPtr.Zero) return;
        bool want = Settings.Current.HiddenUntilHover && !_open && !_pinnedOpen && _toast == null && !_volShown && _callStart == null && !_dragging && _hold == 0 && !_fsHidden && !_hotHidden;
        if (!want) { if (_hidden) { _hidden = false; ApplyOpacity(); } return; }
        Native.GetCursorPos(out var pt);
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX, k = Settings.Current.Scale;
        var tl = _island.PointToScreen(new Point(0, 0));
        double w = Math.Max(_island.ActualWidth, CW) * k * dpi, h = Math.Max(_island.ActualHeight, CH) * k * dpi;
        bool near = pt.X >= tl.X - 8 && pt.X <= tl.X + w + 8 && pt.Y >= tl.Y - 8 && pt.Y <= tl.Y + h + 8;
        if (_hidden == near) { _hidden = !near; ApplyOpacity(); }
    }

    bool SongWide() => Settings.Current.SongTitleInPill && Svc.Media.HasSession && Svc.Media.Playing && Svc.Media.Title.Length > 0 && _toast == null && !Svc.Focus.Running && _callStart == null;
    void Retarget(bool instant = false)
    {
        bool vol = _volShown && !_open, quiet = !_open && !vol && _toast == null;
        bool gamer = _gamer && quiet, sliver = _thin && quiet && !gamer;
        double cw = vol ? VolW : _toast != null ? ToastW : gamer ? _gamerW : sliver ? ThinW : (SongWide() ? 300 : CW);
        bool notch = Settings.Current.Shape == "notch";
        // opening carries a little bounce; closing is critically damped so it never overshoots inward
        double damp = _open ? 0.8 : 1.0, resp = (_open ? 0.36 : 0.28) * (Settings.Current.AnimSpeed == "relaxed" ? 1.3 : Settings.Current.AnimSpeed == "quick" ? 0.67 : 1);
        foreach (var s in new[] { _w, _h, _r }) { s.Damping = damp; s.Response = resp; }
        _w.Target = _open ? EW : cw;
        _h.Target = _open ? (_active?.Height ?? 180) : gamer ? GamerH : sliver ? ThinH : CH;
        double cr = Math.Clamp(Settings.Current.Corners, 0.1, 1);
        _r.Target = _open ? Math.Max(3, 26 * cr) : gamer ? GamerH / 2 : sliver ? ThinH / 2 : Math.Max(2, (notch ? 14 : CH / 2) * cr);
        if (instant || Settings.Current.NoMotion) { _w.Snap(); _h.Snap(); _r.Snap(); Apply(); return; }
        if (_animating) return;
        _animating = true; _clock.Restart();
        CompositionTarget.Rendering += Frame;
    }
    void Frame(object? s, EventArgs e)
    {
        double raw = _clock.Elapsed.TotalSeconds;
        string rate = Settings.Current.MotionRate;
        if (rate == "saver" || (rate == "balanced" && OnBattery()))
            if (raw < 1 / 60.0) return; // battery saver: at most 60 frames a second
        double dt = Math.Min(0.05, raw); _clock.Restart();
        _w.Step(dt); _h.Step(dt); _r.Step(dt);
        Apply();
        if (_w.Settled && _h.Settled && _r.Settled) { _animating = false; CompositionTarget.Rendering -= Frame; }
    }
    bool OnBattery()
    {
        if (DateTime.Now - _batteryAt > TimeSpan.FromSeconds(5))
        {
            _batteryAt = DateTime.Now;
            try { var p = System.Windows.Forms.SystemInformation.PowerStatus; _onBattery = p.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery && p.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline; } catch { }
        }
        return _onBattery;
    }
    // Look settings that are read when the pill is built: the visualizer's shape and colour.
    public void ApplyVisual()
    {
        foreach (Border b in _bars.Children) { b.CornerRadius = new CornerRadius(Settings.Current.VisualizerBars ? 1.25 : 0); b.Background = B(UI.Accent); }
        Retarget();
    }
    void Apply()
    {
        double w = Math.Max(20, _w.Value), h = Math.Max(3, _h.Value), r = Math.Max(0, _r.Value);
        bool notch = Settings.Current.Shape == "notch";
        _island.Width = w; _island.Height = h;
        _island.BorderThickness = new Thickness(h < 12 ? 0 : 1); // a sliver has no room for a rim
        _island.CornerRadius = notch ? new CornerRadius(0, 0, r, r) : new CornerRadius(r);
        _glow.CornerRadius = _island.CornerRadius;
        _island.Clip = notch
            ? new RectangleGeometry(new Rect(0, -r, w, h + r), r, r)
            : new RectangleGeometry(new Rect(0, 0, w, h), r, r);
        // progress from the live height, so content fades track the shape even mid-interruption
        double p = Math.Clamp((h - CH) / Math.Max(1, (_active?.Height ?? 180) - CH), 0, 1);
        _expanded.Opacity = _open ? Math.Clamp((p - 0.25) / 0.6, 0, 1) : Math.Clamp((p - 0.4) / 0.6, 0, 1);
        // the pill's contents fade out as it flattens into the sliver, and as it opens
        _collapsed.Opacity = _volView.Opacity = Math.Clamp(1 - p * 4, 0, 1) * Math.Clamp((h - 14) / 18, 0, 1);
        _gamerText.Opacity = _gamer && !_open ? Math.Clamp((h - 8) / 8, 0, 1) : 0;
        // the panel grows out of the pill instead of only fading: the surface arrives as a material
        _exScale.ScaleX = _exScale.ScaleY = Settings.Current.NoMotion ? 1 : 0.94 + 0.06 * Math.Clamp(p, 0, 1);
        if (_glass != null && Glass.On)
        {
            double k = Settings.Current.Scale, gw = w * k, gh = h * k;
            var pos = Settings.Current.Position;
            double gx = pos == "left" ? 0 : pos == "right" ? Width - gw : (Width - gw) / 2;
            _glass.SetShape(gx, _island.Margin.Top, gw, gh, r * k, notch);
        }
    }

    // ───────── chrome: collapsed content + tint ─────────
    public void RefreshChrome()
    {
        var m = Svc.Media; var f = Svc.Focus;
        bool vol = _volShown && !_open;
        _volView.Visibility = vol ? Visibility.Visible : Visibility.Collapsed;
        _collapsed.Visibility = vol ? Visibility.Hidden : Visibility.Visible;
        // a colour the user chose for the glass wins over the album-art tint
        bool musicTint = Settings.Current.MusicBackground == "album" && !Glass.Coloured && !(_thin && !_open) && m.HasSession && m.Art != null && (_active?.Id == "music" || !_open);
        if (musicTint)
        {
            _island.Background = Glass.Tinted(m.Tint);
        }
        else _island.Background = Glass.Surface(_open);
        if (_gamer && !_open) _island.Background = Settings.Current.GamerBackground == "black" ? B("#F2000000") : Settings.Current.GamerBackground == "none" ? Brushes.Transparent : _island.Background;

        if (_toast != null)
        {
            _cArt.Source = null; _cIcon.Text = _toast.Glyph; _cIcon.Foreground = Brushes.White;
            _cText.Text = string.IsNullOrEmpty(_toast.Body) ? _toast.Title : $"{_toast.Title}  ·  {_toast.Body}";
        }
        else if (_callStart != null && Settings.Current.CallTimer)
        {
            var el = DateTime.Now - _callStart.Value;
            _cArt.Source = null; _cIcon.Text = "\uE720"; _cIcon.Foreground = Brushes.White;
            _cText.Text = el.TotalHours >= 1 ? $"On a call  {(int)el.TotalHours}:{el.Minutes:00}:{el.Seconds:00}" : $"On a call  {el.Minutes:00}:{el.Seconds:00}";
        }
        else if (f.Running)
        {
            _cArt.Source = null; _cIcon.Text = ""; _cIcon.Foreground = Brushes.White;
            _cText.Text = $"{(int)f.Remaining.TotalMinutes:00}:{f.Remaining.Seconds:00}";
        }
        else if (m.HasSession && (m.Playing || m.Art != null))
        {
            _cArt.Source = m.Art; _cIcon.Text = ""; _cIcon.Foreground = B("#8E8E93");
            _cText.Text = Settings.Current.SongTitleInPill && !string.IsNullOrEmpty(m.Title) && m.Playing ? (string.IsNullOrEmpty(m.Artist) ? m.Title : m.Title + "  \u00B7  " + m.Artist) : "";
        }
        else
        {
            _cArt.Source = null; _cIcon.Text = ""; _cIcon.Foreground = B("#8E8E93");
            _cText.Text = RestingText();
        }
        AnimateBars(m.Playing && _toast == null);
        if (!_open) Retarget();
    }
    bool _barsOn;
    void AnimateBars(bool on)
    {
        _bars.Opacity = on ? 1 : 0.35;
        if (on == _barsOn) return;
        _barsOn = on;
        int i = 0;
        foreach (Border b in _bars.Children)
        {
            var st = (ScaleTransform)b.RenderTransform;
            if (on && !Settings.Current.NoMotion)
                st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(380 + i * 90)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(i * 70) });
            else { st.BeginAnimation(ScaleTransform.ScaleYProperty, null); st.ScaleY = 0.3; }
            i++;
        }
    }

    void OnTick()
    {
        Svc.Focus.Tick();
        Watch.Tick();
        PollDevices();
        MeasureBackdrop(); if (Math.Abs(Glass.Veil - _veilTarget) >= 0.02) _veilTimer.Start();
        if (_open) _active?.Tick();
        else if (Settings.Current.Resting != "none" && _toast == null && !Svc.Focus.Running && !(Svc.Media.HasSession && (Svc.Media.Playing || Svc.Media.Art != null)))
        {
            if (Settings.Current.Resting == "stats") Svc.Stats.Sample();
            _cText.Text = RestingText();
        }
        string mode = Settings.Current.Fullscreen;
        // a rule for the program in front beats the general setting
        string exe = _hwnd != IntPtr.Zero ? Native.ForegroundExe(_hwnd) : "";
        var rule = exe.Length == 0 ? null : Settings.Current.AppRules.FirstOrDefault(r => r.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase));
        bool fs = mode != "stay" && _hwnd != IntPtr.Zero && Native.ForegroundIsFullscreen(_hwnd);
        bool hide = fs && mode == "hide", fsThin = fs && mode != "hide";
        bool ruleGamer = false;
        if (rule != null)
        {
            hide = rule.Mode == "hide"; fsThin = rule.Mode is "thin" or "strip"; ruleGamer = rule.Mode == "strip"; fs = hide || fsThin;
        }
        // a maximised window gets the same slim line, so the island never covers its tabs
        bool slim = !fs && rule == null && Settings.Current.SlimUnderMaximized && _hwnd != IntPtr.Zero && Native.ForegroundIsMaximized(_hwnd);
        bool thin = fsThin || slim;
        bool gamer = ruleGamer || (Settings.Current.GamerOverlay && rule == null && (fsThin || Settings.Current.GamerEverywhere));
        if (gamer)
        {
            var gs = Settings.Current; var parts = new List<string>();
            Svc.Stats.Sample(); if (gs.GamerGpu) Svc.Stats.SampleGpu();
            if (gs.GamerCpu) parts.Add($"CPU {Svc.Stats.Cpu * 100:0}%");
            if (gs.GamerGpu) parts.Add($"GPU {(Svc.Stats.Gpu < 0 ? "--" : (Svc.Stats.Gpu * 100).ToString("0"))}%");
            if (gs.GamerRam) parts.Add($"RAM {Svc.Stats.Ram * 100:0}%");
            if (gs.GamerDown) parts.Add("\u2193" + Stats.Rate(Svc.Stats.DownBps));
            if (gs.GamerUp) parts.Add("\u2191" + Stats.Rate(Svc.Stats.UpBps));
            if (gs.GamerClock) parts.Add(UI.TimeText(DateTime.Now));
            string txt = parts.Count == 0 ? "" : string.Join("   ", parts);
            _gamerText.Text = txt;
            // the strip is as wide as what it shows; with nothing chosen it stays hidden
            double w = txt.Length == 0 ? 0 : Math.Clamp(txt.Length * 6.6 + 40, 90, 640);
            if (Math.Abs(w - _gamerW) > 8) { _gamerW = w; if (_gamer) Retarget(); }
            if (txt.Length == 0) gamer = false;
        }
        if (gamer != _gamer) { _gamer = gamer; _gamerText.Opacity = 0; RefreshChrome(); Retarget(); }
        if (hide != _fsHidden && !(hide && _pinnedOpen)) { _fsHidden = hide; if (hide) { Close_(); Hide(); } else Show(); }
        if (thin != _thin)
        {
            // idle over a fullscreen app: a small solid sliver on the edge, with no blur and no screen reads
            _thin = thin; _fsThin = fsThin;
            if (Settings.Current.Position != "free") _island.Margin = new Thickness(0, thin || Settings.Current.Shape == "notch" ? 0 : Settings.Current.TopDistance, 0, 0);
            UpdateSuspend();
            ApplyGlass();
            Retarget();
        }
        if (_fsThin != fsThin) { _fsThin = fsThin; UpdateSuspend(); }
        if (!Topmost) { Topmost = true; _glass?.Follow(); }
    }
}
