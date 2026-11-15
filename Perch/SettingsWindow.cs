using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using static Perch.UI;

namespace Perch;

public partial class SettingsWindow : Window
{
    static SettingsWindow? _instance;
    public static void Open(string page = "General")
    {
        if (_instance == null) { _instance = new SettingsWindow(); _instance.Closed += (_, _) => _instance = null; }
        _instance.Go(page);
        ((Window)_instance).Show(); _instance.Activate();
    }

    static readonly (string Name, string Glyph)[] Pages =
    {
        ("General", ""), ("Accessibility", "\uE7B3"), ("Appearance", ""), ("Island", ""), ("Panels", ""),
        ("Widgets", ""), ("Assistant", ""), ("Calls", "\uE720"), ("Gaming", "\uE7FC"), ("Tools", "\uE90F"), ("Windows", "\uE8A7"), ("Utilities", "\uE90F"), ("Capture", "\uE722"), ("Display", "\uE7F4"), ("Alerts", "\uEA8F"), ("More tabs", "\uE8F1"), ("Features", "\uE713"), ("Language", "\uE774"), ("Input", "\uE765"), ("System", "\uE950"), ("Keys", ""), ("About", "")
    };
    Border _frame;
    readonly StackPanel _nav = new();
    readonly StackPanel _page = new();
    readonly ScrollViewer _scroll;
    string _current = "General", _filter = ""; string? _capture;
    static Settings S => Settings.Current;

    SettingsWindow()
    {
        Title = "Perch settings"; Width = 800; Height = 720; WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true; ShowInTaskbar = true;

        var title = Row(
            new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(6), Background = B("#2C2C2E"), Child = new Border { Width = 11, Height = 5, CornerRadius = new CornerRadius(2.5), Background = Brushes.White } },
            T("Perch", 13, "#FFFFFF", true).M(10), T("1.0", 10.5, Dim, false, true).M(8, 1));
        var close = IconBtn("", Close, 10, "#C7C7CC", 30);
        var head = MakeGrid("*,auto", "auto");
        head.Children.Add(title.At(0, 0)); head.Children.Add(close.At(0, 1));
        head.Background = Brushes.Transparent; head.Margin = new Thickness(20, 14, 14, 8);
        head.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        var search = new TextBox
        {
            Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), CaretBrush = Brushes.White,
            FontFamily = Font, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, MinWidth = 120
        };
        var hint = T("Search settings", 12.5, Dim); hint.IsHitTestVisible = false;
        var sg = new Grid(); sg.Children.Add(hint); sg.Children.Add(search);
        search.TextChanged += (_, _) => { _filter = search.Text.Trim(); hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Render(); };
        var searchBox = new Border { CornerRadius = new CornerRadius(10), BorderBrush = B("#3A3A3C"), BorderThickness = new Thickness(1), Padding = new Thickness(12, 7, 12, 7), Child = Row(Ico("", 12, Dim).M(0, 0, 9), sg) };

        var navScroll = new ScrollViewer { Content = _nav, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        navScroll.PreviewMouseWheel += (_, e) => { navScroll.ScrollToVerticalOffset(navScroll.VerticalOffset - e.Delta * 0.6); e.Handled = true; };
        var side = MakeGrid("*", "auto,*"); side.Children.Add(searchBox.At(0, 0)); side.Children.Add(navScroll.M(0, 12, 0, 0).At(1, 0));
        side.Margin = new Thickness(14, 4, 14, 14); side.Width = 190;

        _scroll = new ScrollViewer { Content = _page, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Padding = new Thickness(34, 4, 34, 24) };
        // scroll by the wheel distance directly, so rows never swallow the wheel and long pages always move
        _scroll.PreviewMouseWheel += (_, e) => { _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset - e.Delta * 0.8); e.Handled = true; };
        var body = MakeGrid("auto,auto,*", "*");
        body.Children.Add(side.At(0, 0));
        body.Children.Add(new Border { Width = 1, Background = B(Line) }.At(0, 1));
        body.Children.Add(_scroll.At(0, 2));

        var root = MakeGrid("*", "auto,*");
        _sheen = new Border { Height = 110, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Background = Glass.Sheen, CornerRadius = new CornerRadius(13, 13, 0, 0), Opacity = 0.6 };
        root.Children.Add(_sheen.At(0, 0, 2, 1));
        root.Children.Add(head.At(0, 0)); root.Children.Add(body.At(1, 0));
        var frame = new Border
        {
            Background = Glass.Panel, CornerRadius = new CornerRadius(14), BorderBrush = Glass.On ? Glass.Rim : B("#2C2C2E"), BorderThickness = new Thickness(1),
            Child = root, Margin = new Thickness(18), Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Opacity = 0.5, Color = Colors.Black }
        };
        Content = frame; _frame = frame;
        ApplyScale();
        SourceInitialized += (_, _) => { _glass = new GlassBackdrop(this) { Reach = 0.75f }; ApplyGlass(); }; // keeps a little more frost: this window is mostly small text
        // clear glass: read the screen just outside each edge and darken the panel when it is bright there
        var watch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        watch.Tick += (_, _) => MeasureBackdrop();
        IsVisibleChanged += (_, _) => { if (IsVisible) { MeasureBackdrop(true); watch.Start(); } else watch.Stop(); };
        Closed += (_, _) => watch.Stop();
        PreviewKeyDown += (_, e) =>
        {
            if (_capture == null) { if (e.Key == Key.Escape) Close(); return; }
            e.Handled = true;
            var k = e.Key == Key.System ? e.SystemKey : e.Key;
            if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            if (k != Key.Escape)
            {
                if (k == Key.Back) S.Hotkeys[_capture] = "";
                else
                {
                    var m = Keyboard.Modifiers;
                    if (m == ModifierKeys.None) return; // a bare key would swallow normal typing everywhere
                    var parts = new List<string>();
                    if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
                    if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
                    if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
                    if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
                    parts.Add(k.ToString());
                    S.Hotkeys[_capture] = string.Join("+", parts);
                }
                S.Save();
                IslandWindow.Instance.RegisterHotkeys();
            }
            _capture = null; Render();
        };
    }

    GlassBackdrop? _glass; Border _sheen; Effect? _shadow;
    void MeasureBackdrop(bool snap = false)
    {
        if (!Glass.On || (!Glass.Clear && !(S.GlassAdaptColor && Glass.Coloured)) || WindowState == WindowState.Minimized) return;
        double d = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int x = (int)((Left + 18) * d), y = (int)((Top + 18) * d), w = (int)((Width - 36) * d), h = (int)((Height - 36) * d), t = (int)(14 * d), gap = (int)(3 * d);
        var seen = new System.Windows.Media.Color[4];
        double brightest = new[]
        {
            Native.ScreenSample(x, y - gap - t, w, t, out seen[0]), Native.ScreenSample(x, y + h + gap, w, t, out seen[1]),
            Native.ScreenSample(x - gap - t, y, t, h, out seen[2]), Native.ScreenSample(x + w + gap, y, t, h, out seen[3])
        }.Max();
        if (brightest < 0) return;
        if (S.GlassAdaptColor && Glass.Coloured)
        {
            var avg = System.Windows.Media.Color.FromRgb((byte)seen.Average(c => c.R), (byte)seen.Average(c => c.G), (byte)seen.Average(c => c.B));
            var was = Glass.PanelBackdrop;
            if (Math.Abs(avg.R - was.R) + Math.Abs(avg.G - was.G) + Math.Abs(avg.B - was.B) > 6) { Glass.PanelBackdrop = avg; _frame.Background = Glass.Panel; }
        }
        double target = Math.Clamp((brightest - 0.18) / 0.35, 0, 1);
        if (Math.Abs(target - Glass.PanelVeil) < 0.02) return;
        Glass.PanelVeil = snap ? target : Glass.PanelVeil + (target - Glass.PanelVeil) * 0.5;
        _frame.Background = Glass.Panel;
    }
    void ApplyScale()
    {
        double k = S.Scale;
        _frame.LayoutTransform = new ScaleTransform(k, k);
        Width = 764 * k + 36; Height = 684 * k + 36;
        ApplyGlass();
    }
    void ApplyGlass()
    {
        _frame.Background = Glass.Panel;
        _frame.BorderBrush = Glass.On ? Glass.Rim : B("#2C2C2E");
        // a drop shadow is drawn under the whole panel, so through glass it would only darken it
        _shadow ??= _frame.Effect;
        _frame.Effect = Glass.On ? null : _shadow;
        _sheen.Visibility = Glass.On ? Visibility.Visible : Visibility.Collapsed;
        double k = S.Scale;
        _glass?.SetShape(18, 18, Width - 36, Height - 36, 14 * k);
        _glass?.Follow();
        var me = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        Native.ExcludeFromCapture(me, S.HideFromCapture);
        if (_glass != null) Native.ExcludeFromCapture(_glass.Handle, S.HideFromCapture);
    }
    void Go(string page) { _current = Pages.Any(p => p.Name == page) ? page : "General"; Render(); _scroll.ScrollToTop(); }

    void Render()
    {
        _nav.Children.Clear();
        foreach (var (name, glyph) in Pages)
        {
            var n = name; bool on = n == _current && _filter.Length == 0;
            var g = MakeGrid("auto,*,auto", "auto");
            g.Children.Add(Ico(glyph, 13, on ? "#FFFFFF" : "#C7C7CC").At(0, 0));
            g.Children.Add(T(name, 12.5, on ? "#FFFFFF" : "#D1D1D6", on).M(12).At(0, 1));
            if (on) g.Children.Add(new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Background = Brushes.White, VerticalAlignment = VerticalAlignment.Center }.At(0, 2));
            var b = Btn(g, () => { _filter = ""; Go(n); }, on ? "#26FFFFFF" : "#00FFFFFF", 9, new Thickness(11, 8, 11, 8));
            b.Margin = new Thickness(0, 0, 0, 2);
            _nav.Children.Add(b);
        }
        _page.Children.Clear();
        if (_filter.Length > 0)
        {
            Head("Search", $"Results for “{_filter}”");
            int before = _page.Children.Count;
            foreach (var (name, _) in Pages) BuildPage(name);
            if (_page.Children.Count == before) _page.Children.Add(T("Nothing matches. Try a shorter word.", 12, Dim).M(0, 12));
        }
        else
        {
            Head("Settings", _current);
            BuildPage(_current);
        }
    }

    // ───────── building blocks ─────────
    void Head(string eyebrow, string title)
    {
        _page.Children.Add(T(eyebrow.ToUpper(), 10, Dim, false, true).M(0, 8));
        var t = T(title, 26, "#FFFFFF", true); t.Margin = new Thickness(0, 4, 0, 6);
        _page.Children.Add(t);
    }
    void Section(string name)
    {
        if (_filter.Length > 0) return;
        _page.Children.Add(T(name.ToUpper(), 10, Dim, false, true).M(0, 22, 0, 8));
        _page.Children.Add(new Border { Height = 1, Background = B(Line) });
    }
    void RowItem(string title, string desc, FrameworkElement? control, bool below = false)
    {
        if (_filter.Length > 0 && !(title + " " + desc).Contains(_filter, StringComparison.OrdinalIgnoreCase)) return;
        var d = T(desc, 11.5, "#AEAEB2"); d.TextWrapping = TextWrapping.Wrap; d.LineHeight = 16;
        var text = Col(T(title, 13, "#FFFFFF", true), d.M(0, 3));
        FrameworkElement content;
        if (below || control == null) content = control == null ? text : Col(text, control.M(0, 10));
        else
        {
            var g = MakeGrid("*,auto", "auto");
            g.Children.Add(text.M(0, 0, 24, 0).At(0, 0));
            control.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(control.At(0, 1));
            content = g;
        }
        content.Margin = new Thickness(0, 14, 0, 14);
        _page.Children.Add(content);
        _page.Children.Add(new Border { Height = 1, Background = B(Line) });
    }
    // Chips that can each be on or off, for picking several things from a list.
    static FrameworkElement MultiChips(string[] labels, Func<int, bool> isOn, Action<int> toggle)
    {
        var wp = new WrapPanel { MaxWidth = 440 };
        for (int i = 0; i < labels.Length; i++)
        {
            int at = i; bool on = isOn(i);
            var chip = Btn(T(labels[i], 11, on ? "#000000" : "#E5E5EA", true), () => toggle(at), on ? "#FFFFFF" : Card, 9, new Thickness(10, 5, 10, 5));
            chip.Margin = new Thickness(0, 0, 6, 6); wp.Children.Add(chip);
        }
        return wp;
    }
    static void InputCommit() { S.Save(); Hooks.Refresh(); }
    // A list of programs, each with a Remove button, and one to add more.
    void AppList(string title, string desc, List<string> list, Action changed)
    {
        RowItem(title, desc, Pill("Add program…", () => AppPicker.Choose(p =>
        {
            var exe = System.IO.Path.GetFileName(p);
            if (!list.Any(x => x.Equals(exe, StringComparison.OrdinalIgnoreCase))) list.Add(exe);
            changed(); Render();
        })));
        foreach (var exe in list.ToList())
        {
            var e2 = exe;
            RowItem(e2, "", Pill("Remove", () => { list.Remove(e2); changed(); Render(); }));
        }
    }
    static void Commit() { S.Save(); IslandWindow.Instance.ApplySettings(); IslandWindow.Instance.RefreshChrome(); }
    static FrameworkElement SliderRow(double value, double min, double max, string unit, Action<double> set, Action<MiniSlider>? style = null)
    {
        var label = T($"{value:0} {unit}", 11.5, "#FFFFFF", false, true); label.Width = 54; label.TextAlignment = TextAlignment.Right;
        var s = new MiniSlider(3) { Width = 150, Value = (value - min) / (max - min) };
        s.Changed += v => { var real = Math.Round(min + v * (max - min)); label.Text = $"{real:0} {unit}"; set(real); };
        s.Committed += _ => S.Save();
        style?.Invoke(s);
        return Row(s, label);
    }
    static Border Field(string value, string placeholder, double width, Action<string> commit, out TextBox box)
    {
        var tb = new TextBox
        {
            Text = value, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), CaretBrush = Brushes.White,
            FontFamily = Font, FontSize = 12, Width = width, VerticalAlignment = VerticalAlignment.Center, ToolTip = placeholder
        };
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) commit(tb.Text.Trim()); };
        box = tb;
        return new Border { CornerRadius = new CornerRadius(9), BorderBrush = B("#3A3A3C"), BorderThickness = new Thickness(1), Padding = new Thickness(10, 6, 10, 6), Child = tb };
    }

    // ───────── pages ─────────
    void BuildPage(string name)
    {
        switch (name)
        {
            case "General":
                Section("General");
                RowItem("Run at startup", "Launch Perch by itself each time you log in.", new Toggle(S.RunAtStartup, v => { S.RunAtStartup = v; Native.SetStartup(v); S.Save(); }));
                RowItem("When the island opens", "Pick which card greets you: the one you used most recently, or the player every time.",
                    new Segmented(new[] { "Last tab", "Music" }, S.OpenOn == "music" ? 1 : 0, i => { S.OpenOn = i == 1 ? "music" : "last"; S.Save(); }));
                RowItem("During fullscreen apps", "What Perch does while a game, video or slideshow fills the main screen. Thin leaves a small bar on the edge; rest the pointer on it and it opens.",
                    new Segmented(new[] { "Thin", "Hide", "Stay" }, S.Fullscreen == "hide" ? 1 : S.Fullscreen == "stay" ? 2 : 0, i => { S.Fullscreen = new[] { "thin", "hide", "stay" }[i]; S.Save(); }));
                RowItem("Open by hovering over fullscreen apps", "Off, Perch ignores the pointer completely while a game or fullscreen video is in front, so looking up or flicking to the edge never touches it. It opens only from its hotkey (Settings, Keys). On, resting the pointer on the thin bar opens it.",
                    new Toggle(S.HoverOpensInGames, v => { S.HoverOpensInGames = v; S.Save(); }));
                RowItem("Thin line under maximised windows", "While a maximised window is in front, Perch collapses to a thin line so it never covers the window's tabs. Rest the pointer on it to open it.",
                    new Toggle(S.SlimUnderMaximized, v => { S.SlimUnderMaximized = v; S.Save(); }));
                RowItem("Stats strip over fullscreen apps", "While a game is fullscreen, the thin bar becomes a slim strip with CPU, GPU, memory and download speed. A game must be borderless or windowed fullscreen for anything to draw over it. Frame rate is not shown.",
                    new Toggle(S.GamerOverlay, v => { S.GamerOverlay = v; S.Save(); }));
                RowItem("Hide from screenshots and recordings", "Perch stays out of screenshots, screen recordings and screen sharing, so nobody watching sees it. You still see it.",
                    new Toggle(S.HideFromCapture, v => { S.HideFromCapture = v; S.Save(); IslandWindow.Instance.ApplyCapture(); ApplyGlass(); }));
                RowItem("Call timer", "While another app is using the microphone, the closed pill counts how long it has been on.",
                    new Toggle(S.CallTimer, v => { S.CallTimer = v; S.Save(); IslandWindow.Instance.RefreshChrome(); }));
                RowItem("Power and sound output pop-ups", "The pill says so when the charger is plugged in or out, and when the sound output changes, for example when headphones are connected.",
                    new Toggle(S.PowerPopups && S.OutputPopups, v => { S.PowerPopups = S.OutputPopups = v; S.Save(); }));
                RowItem("Show microphone changes", "When the microphone is muted or unmuted, in any app, the closed pill says so for a moment.",
                    new Toggle(S.MicReadout, v => { S.MicReadout = v; S.Save(); }));
                RowItem("Show volume changes", "When the system volume or mute changes, the closed pill turns into a level meter for a moment.",
                    new Toggle(S.VolumeDisplay, v => { S.VolumeDisplay = v; S.Save(); }));
                break;

            case "Accessibility":
                Section("Reading");
                RowItem("Text size", "Enlarges the island, everything on it and this window together. Same as Windows follows Text size in Windows' Accessibility settings.",
                    new Segmented(new[] { "Same as Windows", "100%", "115%", "130%" }, S.SizeMode == "windows" ? 0 : S.UiScale >= 1.25 ? 3 : S.UiScale >= 1.1 ? 2 : 1, i =>
                    {
                        if (i == 0) S.SizeMode = "windows"; else { S.SizeMode = "fixed"; S.UiScale = new[] { 1.0, 1.0, 1.15, 1.3 }[i]; }
                        Commit(); ApplyScale();
                    }), true);
                RowItem("Brighter secondary text", "Captions and hints that are normally grey are drawn almost white, on the island and here. Same as Windows turns it on with a Windows contrast theme.",
                    new Segmented(new[] { "Same as Windows", "On", "Off" }, S.HighContrast ? 1 : S.ContrastMode == "windows" ? 0 : S.ContrastMode == "on" ? 1 : 2, i =>
                    {
                        S.HighContrast = false; S.ContrastMode = new[] { "windows", "on", "off" }[i]; S.Save();
                        IslandWindow.Instance.RebuildTabs(); IslandWindow.Instance.ApplyGlass(); ApplyGlass(); Render();
                    }), true);
                RowItem("Glass surfaces", "The island and this window blur what is behind them. Turn off for solid, fully opaque surfaces.",
                    new Toggle(S.Glass, v => { S.Glass = v; S.Save(); IslandWindow.Instance.ApplyGlass(); ApplyGlass(); }));
                RowItem("See-through", "How much of what is behind shows through sharp. At 0% the glass is fully frosted.",
                    SliderRow(Math.Round(S.GlassClarity * 100), 0, 100, "%", v => { S.GlassClarity = v / 100; IslandWindow.Instance.ApplyGlass(); ApplyGlass(); }));
                RowItem("Glass tint", "Clear shows more of what is behind. Tinted adds a darker veil, which keeps text easier to read over bright windows.",
                    new Segmented(new[] { "Clear", "Tinted" }, S.GlassTint == "tinted" ? 1 : 0, i => { S.GlassTint = i == 1 ? "tinted" : "clear"; S.Save(); IslandWindow.Instance.ApplyGlass(); ApplyGlass(); }));
                Section("Motion");
                RowItem("Keep notices longer", "Notices, reminders and other messages stay on the island twice as long, so there is time to read them.",
                    new Toggle(S.KeepNoticesLonger, v => { S.KeepNoticesLonger = v; S.Save(); }));
                RowItem("Reduce motion", "Skip the spring when the island grows or shrinks, and freeze the equaliser bars. Same as Windows follows Animation effects in Windows' Accessibility settings.",
                    new Segmented(new[] { "Same as Windows", "On", "Off" }, S.ReduceMotion ? 1 : S.MotionMode == "windows" ? 0 : S.MotionMode == "on" ? 1 : 2, i =>
                    {
                        S.ReduceMotion = false; S.MotionMode = new[] { "windows", "on", "off" }[i]; Commit();
                    }), true);
                break;

            case "Appearance":
                Section("Appearance");
                RowItem("Shape", "Island is a free-floating pill. Notch is attached to the screen edge and only its lower corners are rounded.",
                    new Segmented(new[] { "Island", "Notch" }, S.Shape == "notch" ? 1 : 0, i => { S.Shape = i == 1 ? "notch" : "island"; Commit(); }));
                RowItem("Surface", "Glass blurs what is behind it. Acrylic frosts it more. Graphite and Black are solid, and Pure black keeps the island's pixels off on OLED screens. Candy glass, Ros\u00E9, Lavender, Peach and Sakura are glass with a soft colour wash of their own.",
                    Chips(Themes.Select(t => t.Label).ToArray(), Math.Max(0, Array.FindIndex(Themes, t => t.Id == S.Theme)), i =>
                    {
                        S.Theme = Themes[i].Id; S.Save();
                        IslandWindow.Instance.RebuildTabs(); IslandWindow.Instance.ApplyGlass(); ApplyGlass(); Render();
                    }), true);
                RowItem("Distance from top", "Gap above the pill. Ignored for the notch shape, which has no gap.",
                    SliderRow(S.TopDistance, 0, 40, "px", v => { S.TopDistance = v; IslandWindow.Instance.ApplySettings(); }));
                RowItem("Size", "The pill, the card and its text scale together.",
                    SliderRow(Math.Round(S.SizeMode == "windows" ? S.Scale * 100 : S.UiScale * 100), 100, 150, "%", v => { S.SizeMode = "fixed"; S.UiScale = v / 100; IslandWindow.Instance.ApplySettings(); ApplyScale(); }));
                RowItem("Corners", "How round the corners are, from square to fully round. The pill and the open card follow it.",
                    SliderRow(Math.Round(S.Corners * 100), 10, 100, "%", v => { S.Corners = v / 100; IslandWindow.Instance.ApplySettings(); }));
                RowItem("Motion", "Fluid moves at your screen's full refresh rate. Battery saver keeps it to 60 frames a second, and Balanced does that only on battery.",
                    new Segmented(new[] { "Fluid", "Balanced", "Battery saver" }, S.MotionRate == "fluid" ? 0 : S.MotionRate == "saver" ? 2 : 1, i => { S.MotionRate = new[] { "fluid", "balanced", "saver" }[i]; S.Save(); }), true);
                RowItem("Visualizer", "What plays beside the artwork on the pill while music plays: bars of square pixels, or smooth rounded bars.",
                    new Segmented(new[] { "Pixels", "Bars" }, S.VisualizerBars ? 1 : 0, i => { S.VisualizerBars = i == 1; S.Save(); IslandWindow.Instance.ApplyVisual(); }));
                RowItem("Accent", "Colours the visualizer, progress and level bars.",
                    Chips(Accents.Select(a => a.Label).ToArray(), Math.Max(0, Array.FindIndex(Accents, a => a.Value == S.Accent)), i =>
                    {
                        S.Accent = Accents[i].Value; S.Save(); IslandWindow.Instance.RebuildTabs(); IslandWindow.Instance.ApplyVisual(); Render();
                    }), true);
                RowItem("Music card background", "Behind the music card: the cover's colours, or the island's plain surface.",
                    new Segmented(new[] { "Album colours", "Plain" }, S.MusicBackground == "plain" ? 1 : 0, i => { S.MusicBackground = i == 1 ? "plain" : "album"; S.Save(); IslandWindow.Instance.RefreshChrome(); }));
                Section("Glass colour");
                RowItem("Take the colour from behind", "The glass picks up the main colour of whatever is on screen around it, and follows as that changes. Turn off to choose a colour yourself.",
                    new Toggle(S.GlassAdaptColor, v => { S.GlassAdaptColor = v; if (v && S.GlassColorStrength < 0.05) S.GlassColorStrength = 0.5; S.Save(); IslandWindow.Instance.ApplyGlass(); ApplyGlass(); Render(); }));
                if (!S.GlassAdaptColor)
                    RowItem("Colour", "The hue of the glass.",
                        SliderRow(Math.Round(S.GlassHue), 0, 360, "\u00B0", v => { S.GlassHue = v; IslandWindow.Instance.ApplyGlass(); ApplyGlass(); }, sl =>
                        {
                            var hues = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                            for (int h = 0; h <= 360; h += 30) hues.GradientStops.Add(new GradientStop(Glass.FromHue(h), h / 360.0));
                            sl.UseTrack(hues);
                        }));
                RowItem("Colour strength", "How much colour the glass carries. At 0% it is neutral, and the player goes back to using the album cover's colour.",
                    SliderRow(Math.Round(S.GlassColorStrength * 100), 0, 100, "%", v => { S.GlassColorStrength = v / 100; IslandWindow.Instance.ApplyGlass(); ApplyGlass(); }));
                Section("Pill");
                RowItem("On the resting pill", "What the closed pill says when there is no music, timer or notice to show.",
                    new Segmented(new[] { "Nothing", "Clock", "CPU and RAM" }, S.Resting == "clock" ? 1 : S.Resting == "stats" ? 2 : 0, i => { S.Resting = new[] { "none", "clock", "stats" }[i]; Commit(); }), true);
                break;

            case "Island":
                Section("Placement");
                RowItem("Position", S.Position == "free"
                        ? "Perch is where you dragged it. Pick a spot here to send it back to the top edge."
                        : "Which part of the top edge Perch lives on, on your primary display.",
                    new Segmented(new[] { "Left", "Center", "Right" }, S.Position == "left" ? 0 : S.Position == "right" ? 2 : S.Position == "free" ? -1 : 1, i => { S.Position = new[] { "left", "center", "right" }[i]; Commit(); Render(); }), true);
                RowItem("Lock position", "Perch can be moved by dragging its top strip: the pill, or the row of tab icons once it is open. Turn this on to stop it moving.",
                    new Toggle(S.LockPosition, v => { S.LockPosition = v; S.Save(); }));
                RowItem("Edge margin", "Gap to the screen corner. Only used for the left and right positions.",
                    SliderRow(S.EdgeMargin, 0, 200, "px", v => { S.EdgeMargin = v; IslandWindow.Instance.ApplySettings(); }));
                {
                    var scr = System.Windows.Forms.Screen.AllScreens;
                    var labels = new List<string> { "Main display" }; for (int i = 0; i < scr.Length; i++) labels.Add($"Screen {i + 1} \u00B7 {scr[i].Bounds.Width} \u00D7 {scr[i].Bounds.Height}");
                    RowItem("Monitor", "The screen the island shows on. Main display follows the one Windows calls main.",
                        Chips(labels.ToArray(), Math.Clamp(S.MonitorIndex + 1, 0, labels.Count - 1), i => { S.MonitorIndex = i - 1; S.Save(); IslandWindow.Instance.ApplySettings(); Render(); }), true);
                }
                RowItem("Hidden until hover", "Nothing on screen until you rest the pointer on the island's edge, where it would be; then the card opens as usual. Volume, notices, reminders and a call in progress still show.",
                    new Toggle(S.HiddenUntilHover, v => { S.HiddenUntilHover = v; S.Save(); }));
                RowItem("Open delay", "How long the pointer rests on the island before it opens. Over browser tabs and games it always waits a little longer.",
                    SliderRow(S.OpenDelayMs, 0, 600, "ms", v => S.OpenDelayMs = (int)v));
                RowItem("Reverse scrolling on the island", "The wheel turns the island's tabs and the volume sliders the other way. Turn it on if your wheel is reversed in Windows and the island still goes opposite to everything else.",
                    new Toggle(S.ReverseScroll, v => { S.ReverseScroll = v; S.Save(); }));
                RowItem("Animation speed", "How fast the island opens, closes and changes shape once it starts. Quick takes about a third less time.",
                    new Segmented(new[] { "Relaxed", "Normal", "Quick" }, S.AnimSpeed == "relaxed" ? 0 : S.AnimSpeed == "quick" ? 2 : 1, i => { S.AnimSpeed = new[] { "relaxed", "normal", "quick" }[i]; S.Save(); }));
                Section("Pop-ups on the pill");
                RowItem("Caps Lock, Num Lock and Scroll Lock", "A message when one of them is turned on or off.", new Toggle(S.CapsHud, v => { S.CapsHud = v; S.Save(); }));
                RowItem("Brightness", "Show screen brightness changes. Works on screens that answer Windows' brightness commands.", new Toggle(S.BrightnessHud, v => { S.BrightnessHud = v; S.Save(); }));
                RowItem("Battery time left", "Windows' estimate under the percentage in the battery pop-up.", new Toggle(S.BatteryTimeLeft, v => { S.BatteryTimeLeft = v; S.Save(); }));
                Section("Now playing");
                RowItem("Show the song title in the closed island", "While music plays, the closed pill shows the song's title and the artist, between the cover and the bars. The pill grows a little wider for it.",
                    new Toggle(S.SongTitleInPill, v => { S.SongTitleInPill = v; S.Save(); IslandWindow.Instance.RefreshChrome(); }));
                break;

            case "Panels":
                Section("Tabs");
                foreach (var t in TabBase.All())
                {
                    var tab = t;
                    RowItem(tab.Name + " tab", TabBlurb(tab.Id), new Toggle(!S.HiddenTabs.Contains(tab.Id), v =>
                    {
                        if (v) S.HiddenTabs.Remove(tab.Id);
                        else if (S.HiddenTabs.Count < TabBase.All().Count - 1) S.HiddenTabs.Add(tab.Id);
                        Commit();
                    }));
                }
                Section("Shelf and launcher");
                RowItem("Captures on the shelf", "Screenshots made with Perch go onto the shelf as they are saved. Off, they are only saved to your Pictures folder.",
                    new Toggle(S.CapturesOnShelf, v => { S.CapturesOnShelf = v; S.Save(); }));
                while (S.Slots.Count < 6) S.Slots.Add("");
                for (int n = 0; n < 6; n++)
                {
                    int slot = n; string path = S.Slots[n];
                    RowItem($"Launcher slot {slot + 1}", path.Length == 0 ? "Empty" : path,
                        Row(Pill("Choose app", () => AppPicker.Choose(p => { S.Slots[slot] = p; S.Save(); Render(); })), path.Length == 0 ? new Border() : Pill("Clear", () => { S.Slots[slot] = ""; S.Save(); Render(); }).M(6)));
                }
                Section("Clipboard privacy");
                RowItem("Apps the clipboard history ignores", "Anything copied while one of these is in front never reaches the history. Add a password manager here.",
                    Pill("Add app…", () => AppPicker.Choose(p => { var exe = System.IO.Path.GetFileName(p); if (!S.ClipIgnoredApps.Any(x => x.Equals(exe, StringComparison.OrdinalIgnoreCase))) S.ClipIgnoredApps.Add(exe); S.Save(); Render(); })));
                foreach (var ign in S.ClipIgnoredApps.ToList())
                {
                    var ig = ign;
                    RowItem(ig, "Ignored by the clipboard history", Pill("Remove", () => { S.ClipIgnoredApps.Remove(ig); S.Save(); Render(); }));
                }
                Section("Snippets");
                RowItem("Add a snippet", "Texts you paste again and again: an address, a sign-off, a meeting link. They show under Snippets in the Clipboard tab.",
                    Field("", "Type or paste a snippet, then press Enter", 300, t => { if (t.Trim().Length > 0) { S.Snippets.Insert(0, t.Trim()); S.Save(); Render(); } }, out _));
                foreach (var snip in S.Snippets.ToList())
                {
                    var sn = snip; string one = sn.Replace('\r', ' ').Replace('\n', ' ');
                    RowItem(one.Length > 54 ? one[..53] + "\u2026" : one, "Saved snippet", Pill("Remove", () => { S.Snippets.Remove(sn); S.Save(); Render(); }));
                }
                Section("Notices");
                RowItem("Repeat Windows notifications", "Toasts from your other apps also appear on the pill and in the Notices tab. Perch only reads their text; nothing is stored after it quits.\nStatus: " + ToastMirror.Status,
                    new Toggle(S.MirrorNotifications, v => { S.MirrorNotifications = v; S.Save(); ToastMirror.Restart(); Render(); }));
                RowItem("Notice time", "Seconds a fresh notice is shown on the pill.",
                    SliderRow(S.NoticeSeconds, 2, 15, "s", v => S.NoticeSeconds = v));
                RowItem("Notify endpoint", "A local web address your own scripts can call to show a message. It listens on this machine only and rejects requests coming from web pages.\n" +
                    $"curl \"http://127.0.0.1:{S.Port}/notify?title=Build&body=done\"\nStatus: {NotifyServer.Status}",
                    new Toggle(S.NotifyEndpoint, v => { S.NotifyEndpoint = v; S.Save(); NotifyServer.Restart(); Render(); }));
                RowItem("Send a test notice", "Fire a sample message to preview it.", Pill("Send", () => Svc.Notices.Add("Test notice", "This is how a notice looks.")));
                break;

            case "Widgets":
                Section("Reminders");
                RowItem("Open the island when a reminder is due", "The island opens on the Reminders tab, even over a game, so you can Snooze or mark it Done. Off, a due reminder only shows as a notice on the pill.",
                    new Toggle(S.ReminderPopups, v => { S.ReminderPopups = v; S.Save(); }));
                RowItem("Reminder sound", "When one goes off, the island shows it with Snooze and Done. Until I answer chimes every 15 seconds for up to a minute.",
                    new Segmented(new[] { "Until I answer", "Once", "Off" }, S.ReminderSound == "once" ? 1 : S.ReminderSound == "off" ? 2 : 0, i => { S.ReminderSound = new[] { "until", "once", "off" }[i]; S.Save(); }), true);
                Section("Formats");
                RowItem("Time format", "System follows Windows' region setting. Every clock in Perch follows this: the resting pill, the game strip, reminders and the desktop clock.",
                    new Segmented(new[] { "System", "12-hour", "24-hour" }, S.TimeFormat == "12" ? 1 : S.TimeFormat == "24" ? 2 : 0, i => { S.TimeFormat = new[] { "system", "12", "24" }[i]; S.Save(); DayClock.Sync(); }));
                RowItem("Network speed", "In megabytes a second, as file downloads show it, or in megabits, as speed tests and internet plans do.",
                    new Segmented(new[] { "MB/s", "Mbps" }, S.SpeedUnit == "Mbps" ? 1 : 0, i => { S.SpeedUnit = i == 1 ? "Mbps" : "MBs"; S.Save(); }));
                Section("Watching in the background");
                RowItem("System alerts", "A notice when the processor stays above 90% for a minute, the system disk is nearly full, or the battery drops below 20%.",
                    new Toggle(S.SystemAlerts, v => { S.SystemAlerts = v; S.Save(); }));
                RowItem("Finished downloads", "A notice when a browser finishes saving a file to your Downloads folder.",
                    new Toggle(S.DownloadNotices, v => { S.DownloadNotices = v; S.Save(); Watch.Restart(); }));
                Section("Clipboard");
                RowItem("Clean copied links", "When you copy a link, tracking tags such as utm_source are removed from it straight away.",
                    new Toggle(S.CleanLinks, v => { S.CleanLinks = v; S.Save(); }));
                RowItem("Clear the clipboard after", "Empties the Windows clipboard this long after the last copy. 0 leaves it alone. Perch's own clipboard history is kept.",
                    SliderRow(S.ClipAutoClearMinutes, 0, 60, "min", v => S.ClipAutoClearMinutes = (int)v));
                Section("Weather");
                TextBox? cityBox = null;
                var status = T(string.IsNullOrEmpty(S.City) ? "" : "Showing " + S.City, 11, Dim);
                async void SetCity(string c)
                {
                    if (c.Length == 0) { S.City = ""; S.Save(); status.Text = ""; _ = Svc.Weather.Refresh(true); return; }
                    status.Text = "Looking it up…";
                    status.Text = await Svc.Weather.SetCity(c) ? "Showing " + S.City : "Couldn't find that place. Check the spelling or your connection.";
                }
                var field = Field(S.City, "City name", 150, SetCity, out cityBox);
                RowItem("City", "Type a city and press Enter or Save. Only the city name is sent, to Open-Meteo, to find its forecast.",
                    Col(Row(field, Pill("Save", () => SetCity(cityBox!.Text.Trim())).M(8)), status.M(2, 6)));
                RowItem("Units", "Temperature on the weather widget.",
                    new Segmented(new[] { "°C", "°F" }, S.Fahrenheit ? 1 : 0, i => { S.Fahrenheit = i == 1; S.Save(); _ = Svc.Weather.Refresh(true); }));
                Section("Desktop clock");
                RowItem("Day and time on the wallpaper", "Large lettering that sits on the desktop behind your windows. Drag to place it, scroll over it to resize.",
                    new Toggle(S.DayClock, v => { S.DayClock = v; S.Save(); DayClock.Sync(); }));
                RowItem("Clock size", "Same as scrolling over the clock.",
                    SliderRow(S.ClockScale * 100, 50, 250, "%", v => { S.ClockScale = v / 100; DayClock.Sync(); }));
                RowItem("Clock colour", "The far left is white; the rest of the strip is the hue of the clock's text.",
                    SliderRow(S.ClockHue < 0 ? 0 : S.ClockHue, 0, 360, "\u00B0", v => { S.ClockHue = v < 4 ? -1 : (int)v; DayClock.Sync(); }, sl =>
                    {
                        var hues = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                        hues.GradientStops.Add(new GradientStop(Colors.White, 0));
                        for (int h = 30; h <= 360; h += 30) hues.GradientStops.Add(new GradientStop(Glass.FromHue(h, 0.55, 1), h / 360.0));
                        sl.UseTrack(hues);
                    }));
                Section("Focus timer");
                RowItem("Session length", "How long one focus session runs. The − and + on the widget change it in steps of five.",
                    SliderRow(S.FocusMinutes, 5, 120, "min", v => { S.FocusMinutes = (int)v; if (!Svc.Focus.Running) { Svc.Focus.Reset(); IslandWindow.Instance.RefreshChrome(); } }));
                break;

            case "Input":
                Section("Mouse wheel");
                RowItem("Reverse the wheel", "Scroll the other way with a mouse wheel. A touchpad keeps its own direction, so the two can disagree, as you may want.",
                    new Toggle(S.ScrollInvert, v => { S.ScrollInvert = v; InputCommit(); }));
                RowItem("Reverse the tilt wheel", "The same for sideways scrolling with a tilt wheel.", new Toggle(S.ScrollInvertH, v => { S.ScrollInvertH = v; InputCommit(); }));
                RowItem("Scroll sideways while holding a key", "Hold the key and the wheel scrolls left and right, for wide tables and timelines.", new Toggle(S.ScrollSideways, v => { S.ScrollSideways = v; InputCommit(); }));
                RowItem("The key to hold", "Alt is the default. Shift already does this in some apps; this makes it work in more of them.",
                    new Segmented(new[] { "Alt", "Shift", "Right Ctrl" }, S.ScrollSidewaysKey == "shift" ? 1 : S.ScrollSidewaysKey == "rctrl" ? 2 : 0, i => { S.ScrollSidewaysKey = new[] { "alt", "shift", "rctrl" }[i]; InputCommit(); }));
                RowItem("Scroll speed", "How far one notch of the wheel goes. 100 is Windows' own.",
                    SliderRow(S.ScrollSpeed, 25, 400, "%", v => { S.ScrollSpeed = v; Hooks.Refresh(); }));
                RowItem("Same distance every notch", "A notch always scrolls the same, however fast the wheel is spun.", new Toggle(S.ScrollLinear, v => { S.ScrollLinear = v; InputCommit(); }));
                RowItem("Smooth scrolling", "A notch glides instead of jumping. Perch sends many small steps; some older apps ignore them.", new Toggle(S.ScrollSmooth, v => { S.ScrollSmooth = v; InputCommit(); }));
                RowItem("Turn pointer acceleration off", "Windows' “enhance pointer precision” makes the pointer travel further when you move quickly. Off, the same hand movement always moves it the same. Turn this off again and your earlier setting comes back.",
                    new Toggle(S.NoMouseAccel, v => { S.NoMouseAccel = v; InputCommit(); }));
                Section("Mouse buttons");
                var acts = Hooks.ButtonActions;
                RowItem("Back button", "What the side button nearest you does.", Chips(acts.Select(a => a.Label).ToArray(), Math.Max(0, Array.FindIndex(acts, a => a.Id == S.BtnBack)), i => { S.BtnBack = acts[i].Id; InputCommit(); Render(); }), true);
                RowItem("Forward button", "What the side button further away does.", Chips(acts.Select(a => a.Label).ToArray(), Math.Max(0, Array.FindIndex(acts, a => a.Id == S.BtnForward)), i => { S.BtnForward = acts[i].Id; InputCommit(); Render(); }), true);
                RowItem("Middle button", "What pressing the wheel does.", Chips(acts.Select(a => a.Label).ToArray(), Math.Max(0, Array.FindIndex(acts, a => a.Id == S.BtnMiddle)), i => { S.BtnMiddle = acts[i].Id; InputCommit(); Render(); }), true);
                Section("Worn hardware");
                RowItem("Extra click filter", "Ignore a click that follows another too quickly, which a worn button sends by accident. Normal clicks are never delayed.", new Toggle(S.ClickFilter, v => { S.ClickFilter = v; InputCommit(); }));
                RowItem("Shortest gap between clicks", "Clicks closer together than this are ignored. A fast double click is about 80 ms.", SliderRow(S.ClickFilterMs, 10, 120, "ms", v => { S.ClickFilterMs = (int)v; Hooks.Refresh(); }));
                RowItem("Keyboard debounce", "Ignore a letter that repeats too quickly, which a worn key sends by accident. Holding a key still repeats as usual.", new Toggle(S.KeyDebounce, v => { S.KeyDebounce = v; InputCommit(); }));
                RowItem("Shortest gap between presses", "The same key pressed again sooner than this is ignored.", SliderRow(S.KeyDebounceMs, 10, 120, "ms", v => { S.KeyDebounceMs = (int)v; Hooks.Refresh(); }));
                Section("Keyboard");
                RowItem("Caps Lock as a super key", "Hold Caps Lock with another key to press Ctrl+Alt+Shift and that key together. That is a combination almost no app uses, so your own shortcuts never collide. Caps Lock stops toggling capitals while this is on.",
                    new Segmented(new[] { "Off", "On" }, S.SuperKey == "caps" ? 1 : 0, i => { S.SuperKey = i == 1 ? "caps" : "off"; InputCommit(); }));
                if (S.SuperKey == "caps")
                {
                    string[] tapIds = { "none", "escape", "commandbar", "island", "caps" };
                    RowItem("A quick tap does", "Pressing and releasing it on its own.", Chips(new[] { "Nothing", "Escape", "Command bar", "Open Perch", "Caps Lock" }, Math.Max(0, Array.IndexOf(tapIds, S.SuperTap)), i => { S.SuperTap = tapIds[i]; InputCommit(); }), true);
                }
                RowItem("Protect against closing by accident", "Alt+F4 and Ctrl+W close the program only after a long hold or a second press, so a slip of the hand does not lose your work.", new Toggle(S.QuitGuard, v => { S.QuitGuard = v; InputCommit(); Render(); }));
                if (S.QuitGuard)
                {
                    RowItem("How to confirm", "Hold the keys for a moment, or press them twice.", new Segmented(new[] { "Hold", "Press twice" }, S.QuitGuardMode == "double" ? 1 : 0, i => { S.QuitGuardMode = i == 1 ? "double" : "hold"; InputCommit(); }));
                    RowItem("Which shortcuts", "", new Segmented(new[] { "Alt+F4", "Ctrl+W", "Both" }, S.QuitGuardKeys == "altf4" ? 0 : S.QuitGuardKeys == "ctrlw" ? 1 : 2, i => { S.QuitGuardKeys = new[] { "altf4", "ctrlw", "both" }[i]; InputCommit(); }));
                    RowItem("Hold time", "How long to hold.", SliderRow(S.QuitGuardHoldMs, 400, 3000, "ms", v => { S.QuitGuardHoldMs = (int)v; Hooks.Refresh(); }));
                    AppList("Only these programs", "Leave empty to protect every program. Add some to protect just those.", S.QuitGuardApps, InputCommit);
                }
                Section("Text snippets");
                RowItem("Expand snippets as you type", "Type a short trigger anywhere and it turns into the full text. The last few letters typed are kept in memory only, to spot a trigger; nothing is stored or sent. Use something you would never type otherwise, like ;sig.",
                    new Toggle(S.SnippetExpand, v => { S.SnippetExpand = v; InputCommit(); }));
                RowItem("Variables", "Inside a snippet, {date}, {time}, {datetime} and {clipboard} are filled in when it expands.", null);
                foreach (var sn in S.TextSnippets.ToList())
                {
                    var snip = sn;
                    var trig = Field(snip.Trigger, "Trigger, like ;sig", 110, t => { snip.Trigger = t; InputCommit(); }, out var tb1);
                    tb1.LostFocus += (_, _) => { snip.Trigger = tb1.Text.Trim(); InputCommit(); };
                    var body = Field(snip.Text, "What it becomes", 230, t => { }, out var tb2);
                    tb2.AcceptsReturn = true; tb2.TextWrapping = TextWrapping.Wrap; tb2.MinHeight = 38; tb2.MaxHeight = 90;
                    tb2.LostFocus += (_, _) => { snip.Text = tb2.Text; InputCommit(); };
                    RowItem("Snippet", "", Row(trig.M(0, 0, 8), body.M(0, 0, 8), Pill("Remove", () => { S.TextSnippets.Remove(snip); InputCommit(); Render(); })), true);
                }
                RowItem("Add a snippet", "A new one starts with a trigger and a line of text for you to change.", Pill("Add snippet", () => { S.TextSnippets.Add(new TextSnippet { Trigger = ";sig", Text = "Best regards" }); InputCommit(); Render(); }));
                Section("Apps to leave alone");
                AppList("Programs the mouse and snippet tweaks skip", "Wheel, button and snippet changes do nothing while the pointer or typing is in one of these.", S.InputIgnoredApps, InputCommit);
                RowItem("Step aside in fullscreen games", "A mouse tweak has to look at every pointer movement. Leave this on and Perch stops looking while a fullscreen game is in front, so games feel exactly as they do without it.",
                    new Toggle(S.InputPauseInGames, v => { S.InputPauseInGames = v; InputCommit(); }));
                Section("Cleaning mode");
                RowItem("Switch the keyboard and mouse off", "Wipe the keys without typing into anything. Hold Esc for three seconds to finish; it also ends by itself after the time below. Ctrl+Alt+Delete always works. Set a shortcut under Keys.",
                    Pill("Start cleaning mode", () => { Close(); CleaningMode.Start(); }, true));
                RowItem("Screen while cleaning", "Black hides everything. A small note leaves your screen showing.", new Segmented(new[] { "Black screen", "Small note" }, S.CleaningStyle == "indicator" ? 1 : 0, i => { S.CleaningStyle = i == 1 ? "indicator" : "black"; S.Save(); }));
                RowItem("Ends by itself after", "A safety net, in seconds.", SliderRow(S.CleaningMaxSeconds, 30, 900, "s", v => { S.CleaningMaxSeconds = (int)v; }));
                break;

            case "System":
                Section("Programs");
                AppList("Quit when the last window closes", "Some programs keep running in the background after you close their window. Add them here and Perch politely asks them to quit a few seconds after their last window goes. It never forces anything.", S.AutoQuitApps, () => S.Save());
                Section("Keep awake");
                RowItem("While certain programs run", "The PC and its screen stay awake as long as any listed program is running, and go back to normal when they are all closed.", new Toggle(S.AwakeWhileApps, v => { S.AwakeWhileApps = v; S.Save(); Render(); }));
                if (S.AwakeWhileApps) AppList("Programs that keep it awake", "A video player or a long download, for example.", S.AwakeApps, () => S.Save());
                RowItem("While plugged in", "No sleep while the charger is connected.", new Toggle(S.AwakeOnCharger, v => { S.AwakeOnCharger = v; S.Save(); }));
                Section("Sound");
                RowItem("Pick the output by priority", "When a sound device is connected or disconnected, Perch switches to the highest one on your list that is present. Your own switches in between are left alone.",
                    new Toggle(S.AudioPriorityOn, v => { S.AudioPriorityOn = v; S.Save(); Render(); }));
                if (S.AudioPriorityOn)
                {
                    var present = Mixer.Outputs();
                    foreach (var id in S.AudioPriority.ToList())
                    {
                        var did = id; int at = S.AudioPriority.IndexOf(did);
                        string nm = present.FirstOrDefault(o => o.Id == did).Name ?? "Not connected right now";
                        RowItem($"{at + 1}.  {nm}", present.Any(o => o.Id == did) ? "Connected." : "Remembered from before.",
                            Row(IconBtn("", () => { if (at > 0) { S.AudioPriority.RemoveAt(at); S.AudioPriority.Insert(at - 1, did); S.Save(); Render(); } }, 11, "#D1D1D6", 28),
                                IconBtn("", () => { if (at < S.AudioPriority.Count - 1) { S.AudioPriority.RemoveAt(at); S.AudioPriority.Insert(at + 1, did); S.Save(); Render(); } }, 11, "#D1D1D6", 28),
                                IconBtn("", () => { S.AudioPriority.Remove(did); S.Save(); Render(); }, 10, "#D1D1D6", 28)));
                    }
                }
                RowItem("Lower the volume when headphones go away", "When the sound output that was in use disappears, the speakers are held to this level, so music never blasts out of them.",
                    new Toggle(S.HeadphoneDropVolume >= 0, v => { S.HeadphoneDropVolume = v ? 30 : -1; S.Save(); Render(); }));
                if (S.HeadphoneDropVolume >= 0) RowItem("Level", "The most the speakers play at after that.", SliderRow(S.HeadphoneDropVolume, 5, 100, "%", v => { S.HeadphoneDropVolume = (int)v; }));
                RowItem("Bluetooth off during sleep", "Switch Bluetooth off when the PC goes to sleep and back on when it wakes, only if Perch turned it off. Your mouse or headphones then do not wake it or drain a laptop.",
                    new Toggle(S.BluetoothSleep, v => { S.BluetoothSleep = v; S.Save(); }));
                Section("Clipboard history");
                RowItem("Keep it between sessions", "History survives a restart. It is encrypted for this Windows account in %AppData%\\Perch, and removed when you turn this off.",
                    new Toggle(S.ClipPersist, v => { S.ClipPersist = v; S.Save(); if (v) Svc.Clip.Save(); else ClipHistory.Forget(); }));
                RowItem("How many clips", "Older ones fall off the end. Starred clips stay.", SliderRow(S.ClipMaxEntries, 20, 500, "", v => { S.ClipMaxEntries = (int)v; }));
                RowItem("Forget after", "Clips older than this go by themselves. 0 keeps them until you clear them.", SliderRow(S.ClipKeepHours, 0, 168, "h", v => { S.ClipKeepHours = (int)v; }));
                Section("Radial menu");
                RowItem("Shortcut", "Opens a ring of round buttons around the pointer. Set it under Keys; it starts as Ctrl+Alt+R.", Pill("Open Keys", () => Go("Keys")));
                RowItem("What is on the ring", "Pick up to eight.", MultiChips(Radial.Catalog.Select(c => c.Label).ToArray(), i => S.RadialItems.Contains(Radial.Catalog[i].Id), i =>
                {
                    var id = Radial.Catalog[i].Id;
                    if (!S.RadialItems.Remove(id)) { if (S.RadialItems.Count >= 8) return; S.RadialItems.Add(id); }
                    S.Save(); Render();
                }), true);
                Section("Settings backup");
                RowItem("Save a copy", "Writes your settings to a file you choose, so you can move them to another PC. Your assistant key is left out.",
                    Pill("Export…", () =>
                    {
                        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "perch-settings.json", Filter = "Settings|*.json" };
                        if (dlg.ShowDialog() != true) return;
                        try
                        {
                            var node = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(S))!.AsObject();
                            node.Remove("AiKey");
                            System.IO.File.WriteAllText(dlg.FileName, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                            Svc.Notices.Add("Settings saved", dlg.FileName, "");
                        }
                        catch (Exception e) { Svc.Notices.Add("Could not save", e.Message, ""); }
                    }));
                RowItem("Load a copy", "Replaces your settings with those in a file, then restarts Perch. Your assistant key stays as it is.",
                    Pill("Import…", () =>
                    {
                        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Settings|*.json" };
                        if (dlg.ShowDialog() != true) return;
                        try
                        {
                            var imported = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(dlg.FileName));
                            if (imported == null) return;
                            imported.AiKey = S.AiKey; imported.Save();
                            var exe = Environment.ProcessPath!;
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & start \"\" \"{exe}\"") { CreateNoWindow = true, UseShellExecute = false });
                            Application.Current.Shutdown();
                        }
                        catch (Exception e) { Svc.Notices.Add("Could not load that file", e.Message, ""); }
                    }));
                break;

            case "Keys":
                Section("Hotkeys");
                foreach (var (id, label) in IslandWindow.HotkeyActions)
                {
                    var aid = id; bool capturing = _capture == id;
                    var combo = S.Hotkey(id);
                    var t = T(capturing ? "Press the keys…" : combo.Length > 0 ? combo : "Not set", 11.5, combo.Length > 0 || capturing ? "#FFFFFF" : Dim, false, true);
                    t.HorizontalAlignment = HorizontalAlignment.Center;
                    var box = Btn(t, () => { _capture = aid; Render(); }, capturing ? "#33FFFFFF" : "#00FFFFFF", 9, new Thickness(14, 7, 14, 7));
                    box.MinWidth = 150; box.BorderBrush = B(capturing ? "#FFFFFF" : "#3A3A3C"); box.BorderThickness = new Thickness(1);
                    RowItem(label, "Status: " + IslandWindow.Instance.HotkeyState.GetValueOrDefault(id, "Not set") + ".", box);
                }
                RowItem("How to change one", "Click a box, then press Ctrl, Alt, Shift or Win together with a key. Backspace clears it, Esc leaves it alone.", null);
                RowItem("Close the open island", "While the island has the keyboard.", KeyCap("Esc"));
                break;

            case "Assistant":
                Section("Your assistant");
                RowItem("API key", "Paste your own Anthropic API key. It is encrypted for this Windows account and sent only to Anthropic, with what you type in the chat. Without a key the Assistant tab cannot answer.",
                    Field(Secret.Open(S.AiKey).Length > 0 ? "•••••••• saved" : "", "sk-ant-…", 300, t => { if (t.Length > 0 && !t.Contains('•')) { S.AiKey = Secret.Seal(t.Trim()); S.Save(); Render(); } }, out _), true);
                RowItem("Forget the key", "Removes the saved key from this PC.", Pill("Forget", () => { S.AiKey = ""; S.Save(); Render(); }));
                RowItem("Model", "Which Claude model answers. Leave it unless you know you want another.",
                    Field(S.AiModel, "claude-sonnet-5-5", 300, t => { if (t.Trim().Length > 0) { S.AiModel = t.Trim(); S.Save(); } }, out _), true);
                Section("Extensions added by the assistant");
                var exts = Extensions.Load();
                if (exts.Count == 0) RowItem("None yet", "Ask the assistant for a feature in the Assistant tab. It writes an extension, you read it, and you press Add.", null);
                foreach (var ex in exts)
                {
                    var e2 = ex;
                    RowItem(e2.Name, (e2.Summary.Length > 0 ? e2.Summary + "  " : "") + (e2.HasScripts ? "Runs PowerShell." : "No scripts."), Pill("Remove", () => { Extensions.Remove(e2.Id); Render(); }));
                }
                RowItem("Open the extensions folder", "Each extension is a small JSON file you can read, edit or share.", Pill("Open", () => { System.IO.Directory.CreateDirectory(Extensions.Dir); Svc.Open(Extensions.Dir); }));
                break;

            case "Calls":
                Section("Calls");
                RowItem("Call timer", "While another app is using the microphone, the closed pill counts how long it has been on.",
                    new Toggle(S.CallTimer, v => { S.CallTimer = v; S.Save(); IslandWindow.Instance.RefreshChrome(); }));
                RowItem("Microphone readout", "Show a message whenever the microphone is muted or unmuted, including by other apps.",
                    new Toggle(S.MicReadout, v => { S.MicReadout = v; S.Save(); }));
                RowItem("Mute shortcut", "Mute or unmute the microphone from anywhere. Set it under Keys.", Pill("Open Keys", () => Go("Keys")));
                break;

            case "Gaming":
                Section("While a game is running");
                RowItem("During fullscreen apps", "Thin leaves a small bar on the edge, Hide takes the island away, and Stay leaves it as it is.",
                    new Segmented(new[] { "Thin", "Hide", "Stay" }, S.Fullscreen == "hide" ? 1 : S.Fullscreen == "stay" ? 2 : 0, i => { S.Fullscreen = new[] { "thin", "hide", "stay" }[i]; S.Save(); }));
                RowItem("Stats strip", "In fullscreen games the thin bar becomes a slim strip of readings. The game must be borderless or windowed fullscreen: in exclusive fullscreen nothing can draw over it.",
                    new Toggle(S.GamerOverlay, v => { S.GamerOverlay = v; S.Save(); }));
                RowItem("Show it everywhere", "Keep the strip up all the time, on the desktop and in every window, not only in fullscreen. Hover it and the card opens as usual.",
                    new Toggle(S.GamerEverywhere, v => { S.GamerEverywhere = v; S.Save(); }));
                RowItem("Open by hovering over fullscreen apps", "Off, Perch ignores the pointer while a game is in front and opens only from its hotkey.",
                    new Toggle(S.HoverOpensInGames, v => { S.HoverOpensInGames = v; S.Save(); }));
                Section("What the strip shows");
                RowItem("CPU", "Processor load.", new Toggle(S.GamerCpu, v => { S.GamerCpu = v; S.Save(); }));
                RowItem("GPU", "Graphics load.", new Toggle(S.GamerGpu, v => { S.GamerGpu = v; S.Save(); }));
                RowItem("RAM", "Memory in use.", new Toggle(S.GamerRam, v => { S.GamerRam = v; S.Save(); }));
                RowItem("Download", "Download rate, in the unit chosen under Widgets.", new Toggle(S.GamerDown, v => { S.GamerDown = v; S.Save(); }));
                RowItem("Upload", "Upload rate, for streaming and big uploads.", new Toggle(S.GamerUp, v => { S.GamerUp = v; S.Save(); }));
                RowItem("Clock", "The time. The strip shrinks to fit what you keep; turn everything off and it stays hidden in games.", new Toggle(S.GamerClock, v => { S.GamerClock = v; S.Save(); }));
                RowItem("Strip background", "Black reads over any game. Glass keeps the island's glass, which asks a little more of your PC. None shows only the numbers.",
                    new Segmented(new[] { "Black", "Glass", "None" }, S.GamerBackground == "glass" ? 1 : S.GamerBackground == "none" ? 2 : 0, i => { S.GamerBackground = new[] { "black", "glass", "none" }[i]; S.Save(); IslandWindow.Instance.RefreshChrome(); }));
                Section("Per-app rules");
                RowItem("Add a rule", "Pick a program and choose what the island does while it is in front. A new rule starts as Hide.",
                    Pill("Add app\u2026", () => AppPicker.Choose(p => { var exe = System.IO.Path.GetFileName(p); if (!S.AppRules.Any(x => x.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase))) S.AppRules.Add(new AppRule { Exe = exe, Mode = "hide" }); S.Save(); Render(); })));
                foreach (var rule in S.AppRules.ToList())
                {
                    var r = rule; string[] modes = { "hide", "strip", "thin", "stay" };
                    RowItem(r.Exe, "What Perch does while this program is in front.",
                        Col(Chips(new[] { "Hide", "Strip", "Thin", "Stay" }, Math.Max(0, Array.IndexOf(modes, r.Mode)), i => { r.Mode = modes[i]; S.Save(); Render(); }), Pill("Remove rule", () => { S.AppRules.Remove(r); S.Save(); Render(); })), true);
                }
                break;

            case "Tools":
                Section("Capture");
                RowItem("Captures on the shelf", "Screenshots made with Perch go onto the shelf as they are saved.", new Toggle(S.CapturesOnShelf, v => { S.CapturesOnShelf = v; S.Save(); }));
                RowItem("Colour picker", "The Pick colour tile in the Utilities tab shows a magnifying cursor; a shortcut copies the colour under the pointer straight away. Set it under Keys.", Pill("Open Keys", () => Go("Keys")));
                Section("Windows");
                RowItem("Focus follows mouse", "Rest the pointer on a window for a moment and it comes to the front, without clicking. Menus, the taskbar and the desktop are left alone.",
                    new Toggle(S.FocusFollowsMouse, v => { S.FocusFollowsMouse = v; S.Save(); }));
                Section("Scripts");
                RowItem("Notify endpoint", "Your scripts can show a message on the island. The switch and port are under Panels.", Pill("Open Panels", () => Go("Panels")));
                break;

            case "About":
                Section("About");
                RowItem("Footprint", "Memory Perch is using right now. Measured with this window open; closed, it uses less.",
                    T($"{System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576.0:0.0} MB", 11.5, "#FFFFFF", false, true));
                RowItem("Perch 1.0", "An original notch for Windows: music, sites, notices, a file shelf, clipboard history, widgets, quick controls and tools at the top of your screen. Not affiliated with any other notch or island app.", null);
                RowItem("Your data", "Settings live in %AppData%\\Perch. Clipboard history stays in memory and is gone when Perch quits; pinned items are saved with your settings.",
                    Pill("Open folder", () => Svc.Open(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Perch"))));
                RowItem("Quit Perch", "Close the island. Run Perch.exe to bring it back.", Pill("Quit", () => Application.Current.Shutdown()));
                break;

            default: BuildExt(name); break;
        }
    }
    static string TabBlurb(string id) => id switch
    {
        "music" => "Controls for the current track from any player: cover, skip, seek, volume.",
        "sites" => "Your bookmarks for daily pages; they open in the default browser.",
        "notices" => "A short history of messages: timers, screenshots and script output.",
        "shelf" => "A temporary holding spot for files between apps.",
        "clip" => "Recent copied text. Click to reuse a clip, pin to keep it.",
        "widgets" => "Calendar strip, focus timer, forecast and CPU, memory and network load.",
        "controls" => "Volume, mute, the power plan and shortcuts to Windows quick settings.",
        "tools" => "Screenshot, snip, record and six slots for apps you launch often.",
        "mixer" => "A volume slider for each app that is making sound, and a switch between speakers and headphones.",
        "utils" => "One-click helpers: keep awake, colour picker, copy text from the screen, clean links and more.",
        "apps" => "Every app with a window open, with a button to ask it to close.",
        "system" => "Ports in use, a speed test, app updates, temporary files and drives.",
        "ai" => "Chat with an assistant, and ask it to add features to the island.",
        "notes" => "A scratchpad that saves itself.",
        "reminders" => "Set a reminder in a few clicks. It slips in when it is due, with Snooze and Done.",
        "manage" => "Uninstall programs, see connected devices, install apps with winget, clean caches and convert pictures.",
        "call" => "Microphone mute, a call timer and your audio devices.",
        _ => ""
    };
    static readonly (string Id, string Label)[] Themes =
    {
        ("glass", "Glass"), ("acrylic", "Acrylic"), ("graphite", "Graphite"), ("black", "Black"), ("pureblack", "Pure black"),
        ("candy", "Candy glass"), ("rose", "Ros\u00E9"), ("lavender", "Lavender"), ("peach", "Peach"), ("sakura", "Sakura")
    };
    static readonly (string Value, string Label, string Hex)[] Accents =
    {
        ("#FFFFFF", "White", "#FFFFFF"), ("windows", "Windows accent", "#0A84FF"), ("#0A84FF", "Blue", "#0A84FF"), ("#BF5AF2", "Purple", "#BF5AF2"), ("#FF375F", "Pink", "#FF375F"),
        ("#FF9F0A", "Orange", "#FF9F0A"), ("#30D158", "Green", "#30D158"), ("#40C8E0", "Teal", "#40C8E0"), ("#FFB3C7", "Blush", "#FFB3C7"), ("#C8B6FF", "Lilac", "#C8B6FF"),
        ("#FFC9A3", "Peach", "#FFC9A3"), ("#FF9EC7", "Sakura pink", "#FF9EC7")
    };
    // A row of choices that wraps, for lists too long for one segmented control.
    static FrameworkElement Chips(string[] labels, int selected, Action<int> pick)
    {
        var wp = new WrapPanel { MaxWidth = 440 };
        for (int i = 0; i < labels.Length; i++)
        {
            int at = i; bool on = i == selected;
            var chip = Btn(T(labels[i], 11, on ? "#000000" : "#E5E5EA", true), () => pick(at), on ? "#FFFFFF" : Card, 9, new Thickness(10, 5, 10, 5));
            chip.Margin = new Thickness(0, 0, 6, 6); wp.Children.Add(chip);
        }
        return wp;
    }
    static Border KeyCap(string keys)
        => new() { CornerRadius = new CornerRadius(9), BorderBrush = B("#3A3A3C"), BorderThickness = new Thickness(1), Padding = new Thickness(14, 7, 14, 7), Child = T(keys, 11.5, "#FFFFFF", false, true) };
}
