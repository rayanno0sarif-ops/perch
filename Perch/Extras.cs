using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static Perch.UI;

namespace Perch;

// ───────────────────────── Per-app audio (Core Audio sessions) ─────────────────────────
[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionManager2
{
    [PreserveSig] int GetAudioSessionControl(IntPtr guid, int flags, out IntPtr control);
    [PreserveSig] int GetSimpleAudioVolume(IntPtr guid, int flags, out IntPtr volume);
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
}
[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionEnumerator
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
}
[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionControl2
{
    [PreserveSig] int GetState(out int state);
    [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid ctx);
    [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
    [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid ctx);
    [PreserveSig] int GetGroupingParam(out Guid group);
    [PreserveSig] int SetGroupingParam(ref Guid group, ref Guid ctx);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
    [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetProcessId(out uint pid);
    [PreserveSig] int IsSystemSoundsSession();
}
[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ISimpleAudioVolume
{
    [PreserveSig] int SetMasterVolume(float level, ref Guid ctx);
    [PreserveSig] int GetMasterVolume(out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int Item(int index, out IMMDevice device);
}
// Windows' own (undocumented) switch for the default playback device; the Sound settings page uses the same call.
[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] class PolicyConfigCom { }
[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat(); [PreserveSig] int GetDeviceFormat(); [PreserveSig] int ResetDeviceFormat(); [PreserveSig] int SetDeviceFormat();
    [PreserveSig] int GetProcessingPeriod(); [PreserveSig] int SetProcessingPeriod(); [PreserveSig] int GetShareMode(); [PreserveSig] int SetShareMode();
    [PreserveSig] int GetPropertyValue(); [PreserveSig] int SetPropertyValue();
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
}

public class AppAudio
{
    public string Name = ""; public string Key = ""; public ImageSource? Icon;
    readonly List<ISimpleAudioVolume> _vols = new(); // one app can hold several sessions; they move together
    internal void Add(ISimpleAudioVolume v) => _vols.Add(v);
    public double Volume
    {
        get { try { _vols[0].GetMasterVolume(out var f); return f; } catch { return 0; } }
        set { var g = Guid.Empty; foreach (var v in _vols) try { v.SetMasterVolume((float)Math.Clamp(value, 0, 1), ref g); } catch { } }
    }
    public bool Muted
    {
        get { try { _vols[0].GetMute(out var m); return m; } catch { return false; } }
        set { var g = Guid.Empty; foreach (var v in _vols) try { v.SetMute(value, ref g); } catch { } }
    }
}

public static class Mixer
{
    // Every app that currently has a sound session on the default output, grouped by program.
    public static List<AppAudio> Apps()
    {
        var list = new List<AppAudio>();
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (en.GetDefaultAudioEndpoint(0, 1, out var dev) != 0 || dev == null) return list;
            var iid = typeof(IAudioSessionManager2).GUID;
            if (dev.Activate(ref iid, 23, IntPtr.Zero, out var o) != 0) return list;
            var mgr = (IAudioSessionManager2)o;
            if (mgr.GetSessionEnumerator(out var sessions) != 0) return list;
            sessions.GetCount(out int n);
            var byKey = new Dictionary<string, AppAudio>();
            for (int i = 0; i < n; i++)
            {
                if (sessions.GetSession(i, out var s) != 0 || s == null) continue;
                s.GetState(out int state); if (state == 2) continue; // expired
                string key, name; ImageSource? icon = null;
                if (s.IsSystemSoundsSession() == 0) { key = "system"; name = "System sounds"; }
                else
                {
                    s.GetProcessId(out uint pid);
                    try
                    {
                        using var p = System.Diagnostics.Process.GetProcessById((int)pid);
                        key = p.ProcessName.ToLowerInvariant();
                        string? path = null; try { path = p.MainModule?.FileName; } catch { }
                        name = p.ProcessName;
                        if (path != null)
                        {
                            try { var d = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileDescription; if (!string.IsNullOrWhiteSpace(d)) name = d; } catch { }
                            icon = Native.FileIcon(path);
                        }
                    }
                    catch { continue; }
                }
                if (!byKey.TryGetValue(key, out var app)) { app = new AppAudio { Key = key, Name = name, Icon = icon }; byKey[key] = app; list.Add(app); }
                try { app.Add((ISimpleAudioVolume)s); } catch { }
            }
        }
        catch { }
        return list.OrderBy(a => a.Key == "system" ? 1 : 0).ThenBy(a => a.Name).ToList();
    }

    public static List<(string Id, string Name, bool Default)> Outputs()
    {
        var list = new List<(string, string, bool)>();
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            string def = ""; if (en.GetDefaultAudioEndpoint(0, 1, out var dd) == 0 && dd != null) dd.GetId(out def);
            if (en.EnumAudioEndpoints(0, 1, out var raw) != 0 || raw == IntPtr.Zero) return list;
            var col = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(raw); Marshal.Release(raw);
            col.GetCount(out int n);
            for (int i = 0; i < n; i++)
            {
                if (col.Item(i, out var d) != 0 || d == null) continue;
                d.GetId(out var id); string name = "Output";
                if (d.OpenPropertyStore(0, out var ps) == 0)
                {
                    var key = new PropertyKey { Id = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Pid = 14 };
                    if (ps.GetValue(ref key, out var pv) == 0 && pv.Ptr != IntPtr.Zero) name = Marshal.PtrToStringUni(pv.Ptr) ?? name;
                }
                list.Add((id, name, id == def));
            }
        }
        catch { }
        return list;
    }
    public static string DefaultOutputId()
    {
        try { var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom(); if (en.GetDefaultAudioEndpoint(0, 1, out var d) == 0 && d != null) { d.GetId(out var id); return id; } } catch { }
        return "";
    }
    // True while some other app has the microphone open (an active capture session on the default input).
    public static bool MicInUse()
    {
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (en.GetDefaultAudioEndpoint(1, 2, out var dev) != 0 || dev == null) return false;
            var iid = typeof(IAudioSessionManager2).GUID;
            if (dev.Activate(ref iid, 23, IntPtr.Zero, out var o) != 0) return false;
            var mgr = (IAudioSessionManager2)o;
            if (mgr.GetSessionEnumerator(out var ss) != 0) return false;
            ss.GetCount(out int n);
            for (int i = 0; i < n; i++)
            {
                if (ss.GetSession(i, out var s) != 0 || s == null) continue;
                s.GetState(out int st); if (st != 1) continue; // 1 = active
                s.GetProcessId(out uint pid); if (pid == (uint)Environment.ProcessId) continue;
                return true;
            }
        }
        catch { }
        return false;
    }
    public static bool SetOutput(string id)
    {
        try { var pc = (IPolicyConfig)new PolicyConfigCom(); for (int role = 0; role < 3; role++) pc.SetDefaultEndpoint(id, role); return true; }
        catch { return false; }
    }
}

// ───────────────────────── Mixer tab ─────────────────────────
public class MixerTab : TabBase
{
    public override string Id => "mixer"; public override string Name => "Mixer"; public override string Glyph => "\uE767";
    public override double Height => 240;
    readonly StackPanel _rows = new(); readonly WrapPanel _outs = new();
    List<AppAudio> _apps = new(); string _sig = ""; readonly List<(AppAudio App, MiniSlider S, TextBlock Pct, TextBlock Ico)> _live = new();
    int _n;

    protected override FrameworkElement Build()
    {
        var scroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Height = 112 };
        scroll.PreviewMouseWheel += (_, e) => { if (e.OriginalSource is DependencyObject d && Parent<MiniSlider>(d) != null) return; scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta * 0.5); e.Handled = true; };
        return Col(Box(scroll, Card, 14, new Thickness(10, 6, 10, 6)), _outs.M(0, 8)).M(12, 4, 12, 0);
    }
    static T? Parent<T>(DependencyObject d) where T : class
    {
        for (var x = d; x != null; x = VisualTreeHelper.GetParent(x)) if (x is T t) return t;
        return null;
    }
    public override void Shown() { Refresh(true); }
    public override void Tick() { if (++_n % 2 == 0) Refresh(false); else Sync(); }

    void Refresh(bool force)
    {
        var apps = Mixer.Apps();
        string sig = string.Join("|", apps.Select(a => a.Key));
        if (!force && sig == _sig) { _apps = apps; Rebind(); Sync(); return; }
        _apps = apps; _sig = sig; _rows.Children.Clear(); _live.Clear();
        if (apps.Count == 0)
        {
            var none = T("Nothing is playing sound right now. Apps appear here when they do.", 11, Dim); none.TextWrapping = TextWrapping.Wrap; none.Margin = new Thickness(2, 8, 2, 8);
            _rows.Children.Add(none);
        }
        foreach (var a in apps)
        {
            var app = a;
            UIElement icon = app.Icon != null ? new Image { Source = app.Icon, Width = 18, Height = 18 } : Ico("\uE8D6", 13, "#C7C7CC");
            var name = T(app.Name, 11.5, "#FFFFFF", true); name.Width = 104; name.ToolTip = app.Name;
            var s = new MiniSlider(4) { Width = 132, Value = app.Volume };
            var pct = T($"{app.Volume * 100:0}%", 10.5, "#FFFFFF", false, true); pct.Width = 36; pct.TextAlignment = TextAlignment.Right;
            var spk = Ico(app.Muted ? "\uE74F" : "\uE995", 12, app.Muted ? "#FF6961" : "#FFFFFF");
            var mute = Btn(spk, () => { var cur = Current(app.Key); if (cur == null) return; cur.Muted = !cur.Muted; Sync(); }, "#00FFFFFF", 8, new Thickness(5));
            mute.ToolTip = "Mute this app";
            s.Changed += v => { Current(app.Key)!.Volume = v; pct.Text = $"{v * 100:0}%"; };
            var row = MakeGrid("auto,auto,*,auto,auto", "auto");
            row.Children.Add(new Border { Width = 22, Child = icon, VerticalAlignment = VerticalAlignment.Center }.At(0, 0));
            row.Children.Add(name.M(8).At(0, 1)); row.Children.Add(s.At(0, 2)); row.Children.Add(pct.At(0, 3)); row.Children.Add(mute.M(2).At(0, 4));
            row.Margin = new Thickness(0, 3, 0, 3);
            _rows.Children.Add(row);
            _live.Add((app, s, pct, spk));
        }
        Outputs();
    }
    AppAudio? Current(string key) => _apps.FirstOrDefault(a => a.Key == key);
    // the session objects are fetched fresh each refresh; point each row at the newest one for its app
    void Rebind() { for (int i = 0; i < _live.Count; i++) { var cur = Current(_live[i].App.Key); if (cur != null) _live[i] = (cur, _live[i].S, _live[i].Pct, _live[i].Ico); } }
    void Sync()
    {
        foreach (var (app, s, pct, ico) in _live)
        {
            if (!s.Dragging) { s.Value = app.Volume; pct.Text = $"{app.Volume * 100:0}%"; }
            bool m = app.Muted; ico.Text = m ? "\uE74F" : "\uE995"; ico.Foreground = B(m ? "#FF6961" : "#FFFFFF");
        }
    }
    void Outputs()
    {
        _outs.Children.Clear();
        // two rows of chips is all the card has room for; the device in use always comes first
        foreach (var o in Mixer.Outputs().OrderBy(d => d.Default ? 0 : 1).Take(4))
        {
            var dev = o;
            string label = dev.Name.Length > 22 ? dev.Name[..21] + "\u2026" : dev.Name;
            var chip = Btn(Row(Ico(dev.Default ? "\uE73E" : "\uE7F5", 10, dev.Default ? "#000000" : "#FFFFFF"), T(label, 10.5, dev.Default ? "#000000" : "#FFFFFF", true).M(6)),
                () => { if (dev.Default) return; if (Mixer.SetOutput(dev.Id)) { Svc.Notices.Add("Sound output changed", dev.Name, "\uE7F5"); _sig = ""; Refresh(true); } else Svc.Notices.Add("Could not switch output", dev.Name, "\uE7BA"); },
                dev.Default ? "#FFFFFF" : Card, 9, new Thickness(9, 5, 9, 5));
            chip.Margin = new Thickness(0, 0, 6, 6); chip.ToolTip = dev.Default ? dev.Name + " (in use)" : "Play sound through " + dev.Name;
            _outs.Children.Add(chip);
        }
    }
}

// ───────────────────────── Notes tab ─────────────────────────
public class NotesTab : TabBase
{
    public override string Id => "notes"; public override string Name => "Notes"; public override string Glyph => "\uE70B";
    public override double Height => 262;
    static string FilePath => System.IO.Path.Combine(Settings.Dir, "notes.txt");
    readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(600) };
    TextBox _box = null!; TextBlock _state = null!; StackPanel _snips = null!;

    protected override FrameworkElement Build()
    {
        string text = ""; try { if (File.Exists(FilePath)) text = File.ReadAllText(FilePath); } catch { }
        _box = new TextBox
        {
            Text = text, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent, Foreground = Brushes.White,
            CaretBrush = Brushes.White, BorderThickness = new Thickness(0), FontFamily = Font, FontSize = 12.5, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            SelectionBrush = B("#66FFFFFF"), Height = 118, Padding = new Thickness(0)
        };
        var hint = T("Type anything. It saves by itself.", 12.5, Dim); hint.VerticalAlignment = VerticalAlignment.Top; hint.IsHitTestVisible = false;
        hint.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _state = T("", 10, Dim);
        _box.TextChanged += (_, _) => { hint.Visibility = _box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; _state.Text = "Saving\u2026"; _save.Stop(); _save.Start(); };
        _save.Tick += (_, _) => { _save.Stop(); Save(); };
        var g = new Grid(); g.Children.Add(hint); g.Children.Add(_box);
        var copy = Pill("Copy all", () => { try { Clipboard.SetText(_box.Text); _state.Text = "Copied"; } catch { } });
        var keep = Pill("Keep as snippet", () => { var t = _box.SelectedText.Length > 0 ? _box.SelectedText : _box.Text; if (t.Trim().Length == 0) return; AddSnippet(t.Trim()); _state.Text = "Snippet saved"; }, false, "\uE735");
        keep.ToolTip = "Saves the selected text, or the whole note if nothing is selected";
        var foot = MakeGrid("*,auto,6,auto", "auto"); foot.Children.Add(_state.At(0, 0)); foot.Children.Add(keep.At(0, 1)); foot.Children.Add(copy.At(0, 3));
        _snips = new StackPanel { Orientation = Orientation.Horizontal };
        var snipScroll = new ScrollViewer { Content = _snips, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        snipScroll.PreviewMouseWheel += (_, e) => { snipScroll.ScrollToHorizontalOffset(snipScroll.HorizontalOffset - e.Delta * 0.6); e.Handled = true; };
        Snips();
        return Col(Box(g, Card, 14, new Thickness(12, 9, 12, 9)), snipScroll.M(0, 7, 0, 0), foot.M(2, 7, 0, 0)).M(12, 4, 12, 0);
    }
    // Saved snippets: one click puts the text on the clipboard, right-click removes it.
    void AddSnippet(string text)
    {
        var l = Settings.Current.Snippets;
        l.Remove(text); l.Insert(0, text); while (l.Count > 12) l.RemoveAt(l.Count - 1);
        Settings.Current.Save(); Snips();
    }
    void Snips()
    {
        _snips.Children.Clear();
        var l = Settings.Current.Snippets;
        if (l.Count == 0) { _snips.Children.Add(T("Saved snippets appear here. Click one to copy it.", 10.5, Dim)); return; }
        foreach (var snip in l.ToList())
        {
            string one = snip.Replace('\r', ' ').Replace('\n', ' ').Trim(), label = one.Length > 26 ? one[..25] + "\u2026" : one;
            var chip = Btn(T(label, 10.5, "#FFFFFF", true), () => { try { Clipboard.SetText(snip); _state.Text = "Snippet copied"; } catch { } }, Card, 9, new Thickness(10, 5, 10, 5));
            chip.Margin = new Thickness(0, 0, 6, 0); chip.ToolTip = (snip.Length > 160 ? snip[..160] + "\u2026" : snip) + "\n\nRight-click to remove";
            chip.MouseRightButtonUp += (_, e) => { Settings.Current.Snippets.Remove(snip); Settings.Current.Save(); Snips(); e.Handled = true; };
            _snips.Children.Add(chip);
        }
    }
    void Save()
    {
        try { Directory.CreateDirectory(Settings.Dir); File.WriteAllText(FilePath, _box.Text); _state.Text = _box.Text.Length == 0 ? "" : "Saved"; }
        catch { _state.Text = "Could not save"; }
    }
}

// ───────────────────────── Utilities tab ─────────────────────────
public class UtilitiesTab : TabBase
{
    public override string Id => "utils"; public override string Name => "Utilities"; public override string Glyph => "\uE90F";
    public override double Height => 296;
    TextBlock _awakeSub = null!; Border _awake = null!;

    static Border Tile(string glyph, string title, string tip, Action click, out TextBlock label)
    {
        label = T(title, 10, "#FFFFFF", true); label.HorizontalAlignment = HorizontalAlignment.Center; label.TextAlignment = TextAlignment.Center;
        var b = Btn(Col(Ico(glyph, 15), label.M(0, 6)), click, Card, 12, new Thickness(2, 10, 2, 9));
        b.ToolTip = tip; ToolTipService.SetInitialShowDelay(b, 450);
        return b;
    }
    protected override FrameworkElement Build()
    {
        var g = MakeGrid("*,6,*,6,*,6,*,6,*", "auto,6,auto,6,auto");
        void Put(int r, int c, Border b) => g.Children.Add(b.At(r * 2, c * 2));
        _awake = Tile("\uE708", "Keep awake", "Stops this PC and its screen from sleeping. Each press picks the next time: 30 minutes, 1 hour, 2 hours, until off, then normal again.", () => { Tools.CycleAwake(); Paint(); }, out _awakeSub);
        Put(0, 0, _awake);
        Put(0, 1, Tile("\uEF3C", "Pick colour", "Click any pixel on screen to copy its colour as HEX.", () => Tools.PickColour(), out _));
        Put(0, 2, Tile("\uE8E9", "Copy text", "Drag a box around text on screen; the words are read on this PC and copied.", () => Tools.GrabText(), out _));
        Put(0, 3, Tile("\uE8D2", "Plain text", "Strip the formatting from whatever text is on the clipboard.", () => Tools.PlainClipboard(), out _));
        Put(0, 4, Tile("\uE71B", "Clean link", "Remove tracking tags from a link on the clipboard.", () => Tools.CleanClipboardLink(true), out _));
        Put(1, 0, Tile("\uE793", "Light / dark", "Switch Windows and apps between light and dark.", () => Tools.ToggleDarkMode(), out _));
        Put(1, 1, Tile("\uE8FC", "Desktop icons", "Show or hide the icons on the desktop.", () => Tools.ToggleDesktopIcons(), out _));
        Put(1, 2, Tile("\uE72E", "Lock", "Lock this PC.", () => Tools.Lock(), out _));
        Put(1, 3, Tile("\uE74D", "Empty bin", "Empty the Recycle Bin. Windows asks you to confirm first.", () => Tools.EmptyBin(), out _));
        Put(1, 4, Tile("\uE968", "Copy my IP", "Copy this PC's address on the local network.", () => Tools.CopyLocalIp(), out _));
        Put(2, 0, Tile("\uE7B3", "Hidden files", "Show or hide hidden files and folders in Explorer.", () => Tools2.ToggleHidden(), out _));
        Put(2, 1, Tile("\uE8A5", "Extensions", "Show or hide file extensions like .pdf.", () => Tools2.ToggleExtensions(), out _));
        Put(2, 2, Tile("\uE7F4", "Display off", "Turn the screens off while the PC keeps running.", () => Tools2.DisplayOff(), out _));
        Put(2, 3, Tile("\uE7F7", "Screen saver", "Start the screen saver now.", () => Tools2.ScreenSaver(), out _));
        Put(2, 4, Tile("\uE711", "Quit others", "Ask every other program to close, except the one in front.", () => Tools2.QuitOthers(false), out _));
        Paint();
        // a one-line calculator and unit converter
        _calc = new TextBox
        {
            Background = Brushes.Transparent, Foreground = Brushes.White, CaretBrush = Brushes.White, BorderThickness = new Thickness(0),
            FontFamily = Font, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MinWidth = 150
        };
        var hint = T("Calculate or convert:  18 * 4.5   or   12 km in mi", 12, Dim); hint.IsHitTestVisible = false;
        _answer = T("", 12, "#FFFFFF", true, true); _answer.HorizontalAlignment = HorizontalAlignment.Right;
        _calc.TextChanged += (_, _) => { hint.Visibility = _calc.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; var r = Calc.Evaluate(_calc.Text); _answer.Text = r == null ? "" : "= " + r; };
        _calc.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || _answer.Text.Length < 3) return;
            try { Clipboard.SetText(_answer.Text[2..]); Svc.Notices.Add("Result copied", _answer.Text[2..], "="); } catch { }
            e.Handled = true;
        };
        var field = new Grid(); field.Children.Add(hint); field.Children.Add(_calc);
        var line = MakeGrid("*,auto", "auto"); line.Children.Add(field.At(0, 0)); line.Children.Add(_answer.M(10).At(0, 1));
        var calcBox = new Border { CornerRadius = new CornerRadius(10), BorderBrush = Glass.CardRim, BorderThickness = new Thickness(1), Background = B("#14FFFFFF"), Padding = new Thickness(11, 7, 11, 7), Child = line };
        calcBox.ToolTip = "Press Enter to copy the answer";
        // the whole box is the field: a click on its padding still puts the caret in it
        calcBox.Cursor = Cursors.IBeam;
        calcBox.MouseLeftButtonDown += (_, e) => { Window.GetWindow(_calc)?.Activate(); _calc.Focus(); Keyboard.Focus(_calc); e.Handled = true; };
        _status = T("", 10.5, Dim);
        Status();
        return Col(g, calcBox.M(0, 8), _status.M(2, 8)).M(12, 4, 12, 0);
    }
    void Paint() { _awakeSub.Text = Tools.AwakeLabel; _awake.BorderBrush = Tools.KeepAwake ? Brushes.White : Glass.CardRim; }
    public override void Shown() { if (_awake != null) { Paint(); Status(); } }
    TextBox _calc = null!; TextBlock _answer = null!, _status = null!; int _n;
    static long _rx0 = -1, _tx0;
    public override void Tick() { if (++_n % 5 == 0) Status(); }
    // System disk, battery and how much data has moved since Perch started.
    void Status()
    {
        try
        {
            var parts = new List<string>();
            var sys = new DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            parts.Add($"{sys.Name.TrimEnd('\\')} {sys.AvailableFreeSpace / 1073741824.0:0} GB free");
            var p = System.Windows.Forms.SystemInformation.PowerStatus;
            if (p.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery && p.BatteryLifePercent <= 1)
                parts.Add($"Battery {p.BatteryLifePercent * 100:0}%" + (p.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? ", charging" : ""));
            long rx = 0, tx = 0;
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up || ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                var st = ni.GetIPv4Statistics(); rx += st.BytesReceived; tx += st.BytesSent;
            }
            if (_rx0 < 0) { _rx0 = rx; _tx0 = tx; }
            parts.Add($"Since Perch started: {Size(rx - _rx0)} down, {Size(tx - _tx0)} up");
            _status.Text = string.Join("   \u00B7   ", parts);
        }
        catch { }
    }
    static string Size(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):0.0} GB" : b >= 1 << 20 ? $"{b / (double)(1 << 20):0} MB" : $"{Math.Max(0, b) / 1024.0:0} KB";
}

// ───────────────────────── Calculator and unit converter ─────────────────────────
public static class Calc
{
    // each unit as (dimension, size in that dimension's base unit)
    static readonly Dictionary<string, (string Dim, double K)> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mm"] = ("len", 0.001), ["cm"] = ("len", 0.01), ["m"] = ("len", 1), ["km"] = ("len", 1000), ["in"] = ("len", 0.0254), ["inch"] = ("len", 0.0254), ["ft"] = ("len", 0.3048), ["yd"] = ("len", 0.9144), ["mi"] = ("len", 1609.344), ["mile"] = ("len", 1609.344), ["miles"] = ("len", 1609.344),
        ["mg"] = ("mass", 0.001), ["g"] = ("mass", 1), ["kg"] = ("mass", 1000), ["oz"] = ("mass", 28.349523125), ["lb"] = ("mass", 453.59237), ["lbs"] = ("mass", 453.59237),
        ["ml"] = ("vol", 0.001), ["l"] = ("vol", 1), ["gal"] = ("vol", 3.785411784), ["cup"] = ("vol", 0.2365882365),
        ["s"] = ("time", 1), ["sec"] = ("time", 1), ["min"] = ("time", 60), ["h"] = ("time", 3600), ["hr"] = ("time", 3600), ["day"] = ("time", 86400), ["days"] = ("time", 86400), ["week"] = ("time", 604800),
        ["b"] = ("data", 1), ["kb"] = ("data", 1024), ["mb"] = ("data", 1048576), ["gb"] = ("data", 1073741824), ["tb"] = ("data", 1099511627776),
        ["kmh"] = ("speed", 1 / 3.6), ["kph"] = ("speed", 1 / 3.6), ["mph"] = ("speed", 0.44704), ["ms"] = ("speed", 1), ["knot"] = ("speed", 0.514444),
    };
    static readonly System.Text.RegularExpressions.Regex Convert = new(@"^\s*(-?[\d.,]+)\s*([a-zA-Z\u00B0]+)\s+(?:in|to|as)\s+([a-zA-Z\u00B0]+)\s*$", System.Text.RegularExpressions.RegexOptions.Compiled);
    static readonly System.Text.RegularExpressions.Regex Sum = new(@"^[\d\s+\-*/().,%]+$", System.Text.RegularExpressions.RegexOptions.Compiled);
    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
    static string Show(double v) => (Math.Abs(v) >= 1e12 || (Math.Abs(v) < 1e-4 && v != 0) ? v.ToString("0.####e+0", Inv) : Math.Round(v, 6).ToString("#,0.######", Inv));

    public static string? Evaluate(string text)
    {
        text = text.Trim(); if (text.Length == 0) return null;
        try
        {
            var m = Convert.Match(text);
            if (m.Success && double.TryParse(m.Groups[1].Value.Replace(",", ""), System.Globalization.NumberStyles.Float, Inv, out var n))
            {
                string from = m.Groups[2].Value.TrimStart('\u00B0').ToLowerInvariant(), to = m.Groups[3].Value.TrimStart('\u00B0').ToLowerInvariant();
                if (from is "c" or "f" or "k" && to is "c" or "f" or "k")
                {
                    double c = from == "c" ? n : from == "f" ? (n - 32) * 5 / 9 : n - 273.15;
                    double r = to == "c" ? c : to == "f" ? c * 9 / 5 + 32 : c + 273.15;
                    return Show(r) + (to == "k" ? " K" : " \u00B0" + to.ToUpperInvariant());
                }
                if (Units.TryGetValue(from, out var a) && Units.TryGetValue(to, out var b) && a.Dim == b.Dim) return Show(n * a.K / b.K) + " " + to;
                return null;
            }
            if (!Sum.IsMatch(text) || !text.Any(char.IsDigit) || !text.Any(ch => "+-*/%".Contains(ch))) return null;
            var value = new System.Data.DataTable { Locale = Inv }.Compute(text.Replace(",", ""), null);
            double d = System.Convert.ToDouble(value, Inv);
            return double.IsNaN(d) || double.IsInfinity(d) ? null : Show(d);
        }
        catch { return null; }
    }
}

// ───────────────────────── The tools themselves ─────────────────────────
public static class Tools
{
    // ── keep awake ──
    // Each press moves to the next choice: 30 minutes, 1 hour, 2 hours, until turned off, then back to normal.
    static bool _awake; static DateTime _awakeUntil = DateTime.MaxValue; static int _awakeStep;
    static readonly System.Windows.Threading.DispatcherTimer _awakeTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    static readonly (string Label, int Minutes)[] AwakeSteps = { ("30 min", 30), ("1 hour", 60), ("2 hours", 120), ("Until off", 0) };
    public static bool KeepAwake => _awake;
    static bool _ruleAwake;
    // a rule (a program is running, or the charger is in) keeps the PC awake without touching the manual choice
    public static void RuleAwake(bool on) { _ruleAwake = on; SetThreadExecutionState(on || _awake ? 0x80000003u : 0x80000000u); }
    public static string AwakeLabel => !_awake ? "Keep awake" : _awakeUntil == DateTime.MaxValue ? "Awake" : $"Awake {Math.Max(1, (int)Math.Ceiling((_awakeUntil - DateTime.Now).TotalMinutes))} min";
    public static void CycleAwake()
    {
        if (_awakeStep >= AwakeSteps.Length) { _awakeStep = 0; SetAwake(false, 0); return; }
        var step = AwakeSteps[_awakeStep++];
        SetAwake(true, step.Minutes);
    }
    static void SetAwake(bool on, int minutes)
    {
        _awake = on;
        _awakeUntil = on && minutes > 0 ? DateTime.Now.AddMinutes(minutes) : DateTime.MaxValue;
        // continuous + system required + display required, or back to continuous alone
        SetThreadExecutionState(on || _ruleAwake ? 0x80000000u | 0x1u | 0x2u : 0x80000000u);
        _awakeTimer.Stop();
        if (on && minutes > 0) { _awakeTimer.Tick -= AwakeCheck; _awakeTimer.Tick += AwakeCheck; _awakeTimer.Start(); }
        Svc.Notices.Add(on ? (minutes > 0 ? $"Keeping this PC awake for {AwakeSteps.First(s => s.Minutes == minutes).Label}" : "Keeping this PC awake") : "Sleep is back to normal",
            on ? "Press Keep awake again to change it." : "", "\uE708");
    }
    static void AwakeCheck(object? s, EventArgs e) { if (_awake && DateTime.Now >= _awakeUntil) { _awakeStep = 0; SetAwake(false, 0); } }

    // ── colour under the pointer, and screenshots, for hotkeys ──
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
    public static void CopyColourUnderCursor()
    {
        Native.GetCursorPos(out var p);
        var dc = GetDC(IntPtr.Zero); uint c = GetPixel(dc, p.X, p.Y); ReleaseDC(IntPtr.Zero, dc);
        string hex = Tools2.Colour((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF));
        try { Clipboard.SetText(hex); Svc.Notices.Add("Colour copied", hex, "\uEF3C"); } catch { }
    }
    public static async void ScreenshotToShelf()
    {
        IslandWindow.Instance.Vanish(true);
        await Task.Delay(180);
        var file = Native.Screenshot();
        IslandWindow.Instance.Vanish(false);
        if (file == null) { Svc.Notices.Add("Screenshot failed", "Windows refused the capture.", ""); return; }
        bool shelf = Settings.Current.CapturesOnShelf;
        if (shelf) Svc.AddToShelf(new[] { file });
        Svc.Notices.Add("Screenshot saved", System.IO.Path.GetFileName(file) + (shelf ? " is on the shelf." : " was saved."), "");
    }

    // Paste the clipboard's text without its formatting, straight into whatever has the keyboard.
    public static async void PastePlain()
    {
        try { if (Clipboard.ContainsText()) { var t = Clipboard.GetText(); Clipboard.SetText(t, TextDataFormat.UnicodeText); } } catch { }
        await Task.Delay(80);
        Native.SendPaste();
    }

    // ── clipboard helpers ──
    public static void PlainClipboard()
    {
        try
        {
            if (!Clipboard.ContainsText()) { Svc.Notices.Add("No text on the clipboard", "", "\uE8D2"); return; }
            var t = Clipboard.GetText(); Clipboard.Clear(); Clipboard.SetText(t, TextDataFormat.UnicodeText);
            Svc.Notices.Add("Formatting removed", "The clipboard now holds plain text.", "\uE8D2");
        }
        catch { Svc.Notices.Add("Clipboard is busy", "Try again in a moment.", "\uE7BA"); }
    }
    static readonly string[] Trackers = { "utm_", "fbclid", "gclid", "gclsrc", "dclid", "gbraid", "wbraid", "msclkid", "yclid", "igshid", "igsh", "mc_eid", "mc_cid", "_hsenc", "_hsmi", "vero_id", "twclid", "ttclid", "s_cid", "ref_src", "ref_url", "spm", "si", "srsltid", "oly_enc_id", "oly_anon_id", "_openstat", "mkt_tok" };
    // Returns the link without tracking tags, or null when the text is not a link or has nothing to remove.
    public static string? CleanLink(string text)
    {
        text = text.Trim();
        if (text.Contains(' ') || text.Contains('\n') || !Uri.TryCreate(text, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https") || u.Query.Length < 2) return null;
        var kept = new List<string>(); bool removed = false;
        foreach (var part in u.Query[1..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string name = part.Split('=')[0].ToLowerInvariant();
            if (Trackers.Any(t => t.EndsWith('_') ? name.StartsWith(t) : name == t)) removed = true; else kept.Add(part);
        }
        if (!removed) return null;
        var b = new UriBuilder(u) { Query = string.Join("&", kept) };
        string s = b.Uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
        return kept.Count == 0 ? s.TrimEnd('?') : s;
    }
    static string _lastCleaned = "";
    public static void CleanClipboardLink(bool say)
    {
        try
        {
            if (!Clipboard.ContainsText()) { if (say) Svc.Notices.Add("No link on the clipboard", "", "\uE71B"); return; }
            var t = Clipboard.GetText(); if (t == _lastCleaned) { if (say) Svc.Notices.Add("That link is already clean", "", "\uE71B"); return; }
            var clean = CleanLink(t);
            if (clean == null) { if (say) Svc.Notices.Add("Nothing to remove", "The clipboard has no link with tracking tags.", "\uE71B"); return; }
            _lastCleaned = clean; Clipboard.SetText(clean);
            Svc.Notices.Add("Link cleaned", clean.Length > 60 ? clean[..59] + "\u2026" : clean, "\uE71B");
        }
        catch { if (say) Svc.Notices.Add("Clipboard is busy", "Try again in a moment.", "\uE7BA"); }
    }
    public static void CopyLocalIp()
    {
        try
        {
            string? ip = null;
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up || ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                var props = ni.GetIPProperties(); if (props.GatewayAddresses.Count == 0) continue; // the adapter that actually reaches the network
                ip = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString();
                if (ip != null) break;
            }
            if (ip == null) { Svc.Notices.Add("No network address", "This PC is not connected.", "\uE968"); return; }
            Clipboard.SetText(ip); Svc.Notices.Add("Address copied", ip, "\uE968");
        }
        catch { Svc.Notices.Add("Could not read the address", "", "\uE7BA"); }
    }

    // ── Windows switches ──
    public static void Lock() => LockWorkStation();
    public static void ToggleDarkMode()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", true);
            if (k == null) return;
            int light = (k.GetValue("AppsUseLightTheme") as int? ?? 1) == 1 ? 0 : 1;
            k.SetValue("AppsUseLightTheme", light, Microsoft.Win32.RegistryValueKind.DWord);
            k.SetValue("SystemUsesLightTheme", light, Microsoft.Win32.RegistryValueKind.DWord);
            SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "ImmersiveColorSet", 2, 300, out _);
            Svc.Notices.Add(light == 1 ? "Light mode" : "Dark mode", "Some apps follow after a moment.", "\uE793");
        }
        catch { Svc.Notices.Add("Could not switch the theme", "", "\uE7BA"); }
    }
    public static void ToggleDesktopIcons()
    {
        try
        {
            IntPtr view = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
            for (IntPtr w = IntPtr.Zero; view == IntPtr.Zero && (w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero;)
                view = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view == IntPtr.Zero) { Svc.Notices.Add("Could not find the desktop", "", "\uE7BA"); return; }
            SendMessage(view, 0x0111, new IntPtr(0x7402), IntPtr.Zero); // the desktop's own "Show desktop icons" command
        }
        catch { }
    }
    public static void EmptyBin()
    {
        IslandWindow.Instance.Hold(true);
        try { SHEmptyRecycleBin(IntPtr.Zero, null, 0); } catch { } // flags 0: Windows shows its own confirmation
        IslandWindow.Instance.Hold(false);
    }

    // ── screen tools ──
    public static void PickColour() => new ScreenPicker(false).Show();
    public static void GrabText() => new ScreenPicker(true).Show();

    internal static async Task ReadText(System.Drawing.Rectangle area)
    {
        try
        {
            using var bmp = new System.Drawing.Bitmap(area.Width, area.Height);
            using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size);
            var code = Qr.Decode(bmp);
            if (code != null) { Clipboard.SetText(code); Svc.Notices.Add("Code copied", code.Length > 60 ? code[..59] + "\u2026" : code, "\uE8E9"); return; }
            // small text reads far better when enlarged first
            int k = area.Height < 60 ? 3 : area.Height < 200 ? 2 : 1;
            using var big = new System.Drawing.Bitmap(bmp, new System.Drawing.Size(Math.Min(bmp.Width * k, 4000), Math.Min(bmp.Height * k, 4000)));
            using var ms = new MemoryStream(); big.Save(ms, System.Drawing.Imaging.ImageFormat.Png); ms.Position = 0;
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
            using var soft = await decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
            var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) { Svc.Notices.Add("Text reading is not available", "Windows has no reading language installed.", "\uE7BA"); return; }
            var result = await engine.RecognizeAsync(soft);
            string text = string.Join(Environment.NewLine, result.Lines.Select(l => l.Text)).Trim();
            if (text.Length == 0) { Svc.Notices.Add("No text found there", "", "\uE8E9"); return; }
            Clipboard.SetText(text);
            Svc.Notices.Add("Text copied", text.Replace(Environment.NewLine, " ").Length > 60 ? text.Replace(Environment.NewLine, " ")[..59] + "\u2026" : text.Replace(Environment.NewLine, " "), "\uE8E9");
        }
        catch (Exception e) { Svc.Notices.Add("Could not read the text", e.Message.Length > 70 ? e.Message[..70] : e.Message, "\uE7BA"); }
    }

    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
    [DllImport("user32.dll")] static extern bool LockWorkStation();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string? cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, string l, uint flags, uint timeout, out IntPtr result);
}

// A full-screen, nearly invisible layer used to point at the screen: one click to pick a colour,
// or a dragged box to read the text inside it. Escape or right-click cancels.
public class ScreenPicker : Window
{
    readonly bool _text; Point? _from; readonly Border _box = new() { BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5), Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), CornerRadius = new CornerRadius(4), Visibility = Visibility.Collapsed };
    readonly Border _chip; readonly Border _swatch = new() { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), BorderBrush = B("#66FFFFFF"), BorderThickness = new Thickness(1) };
    readonly TextBlock _hex = T("", 12, "#FFFFFF", true, true); readonly Canvas _canvas = new();
    string _value = ""; System.Drawing.Bitmap? _snap; int _snapX, _snapY;

    public ScreenPicker(bool text)
    {
        _text = text;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; Title = "Perch picker";
        Background = new SolidColorBrush(Color.FromArgb(text ? (byte)40 : (byte)1, 0, 0, 0)); // a pixel-faint veil so the layer receives the pointer
        Left = SystemParameters.VirtualScreenLeft; Top = SystemParameters.VirtualScreenTop; Width = SystemParameters.VirtualScreenWidth; Height = SystemParameters.VirtualScreenHeight;
        Cursor = Cursors.Cross;
        if (!text)
        {
            try
            {
                var vs = System.Windows.Forms.SystemInformation.VirtualScreen; _snapX = vs.Left; _snapY = vs.Top;
                _snap = new System.Drawing.Bitmap(vs.Width, vs.Height);
                using var g = System.Drawing.Graphics.FromImage(_snap); g.CopyFromScreen(vs.Left, vs.Top, 0, 0, vs.Size);
            }
            catch { _snap = null; }
            Closed += (_, _) => _snap?.Dispose();
        }
        var tip = T(text ? "Drag a box around the text  \u00B7  Esc to cancel" : "Click a pixel to copy its colour  \u00B7  Esc to cancel", 11.5, "#FFFFFF", true);
        _chip = new Border { Background = B("#E6161618"), BorderBrush = Glass.Rim, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 7, 12, 7), IsHitTestVisible = false,
            Child = text ? tip : Row(_swatch, _hex.M(9), T("Click to copy  \u00B7  Esc to cancel", 11, Dim).M(12)) };
        _canvas.Children.Add(_box); _canvas.Children.Add(_chip);
        Content = _canvas;
        Loaded += (_, _) => { Activate(); Focus(); Move(Mouse.GetPosition(this)); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();
        MouseMove += (_, e) => Move(e.GetPosition(this));
        MouseLeftButtonDown += (_, e) => { if (_text) { _from = e.GetPosition(this); CaptureMouse(); } };
        MouseLeftButtonUp += async (_, e) =>
        {
            if (!_text)
            {
                try { Clipboard.SetText(_value); Svc.Notices.Add("Colour copied", _value, "\uEF3C"); } catch { }
                Close(); return;
            }
            if (_from == null) return;
            ReleaseMouseCapture();
            var a = PointToScreen(_from.Value); var b = PointToScreen(e.GetPosition(this));
            var area = new System.Drawing.Rectangle((int)Math.Min(a.X, b.X), (int)Math.Min(a.Y, b.Y), (int)Math.Abs(a.X - b.X), (int)Math.Abs(a.Y - b.Y));
            Close();
            if (area.Width < 8 || area.Height < 8) return;
            await Task.Delay(120); // let the layer leave the screen before it is captured
            await Tools.ReadText(area);
        };
    }
    void Move(Point p)
    {
        if (_text)
        {
            if (_from != null)
            {
                _box.Visibility = Visibility.Visible;
                Canvas.SetLeft(_box, Math.Min(p.X, _from.Value.X)); Canvas.SetTop(_box, Math.Min(p.Y, _from.Value.Y));
                _box.Width = Math.Abs(p.X - _from.Value.X); _box.Height = Math.Abs(p.Y - _from.Value.Y);
            }
        }
        else
        {
            var s = PointToScreen(p);
            byte r, g, b; int sx = (int)s.X - _snapX, sy = (int)s.Y - _snapY;
            if (_snap != null && sx >= 0 && sy >= 0 && sx < _snap.Width && sy < _snap.Height) { var px = _snap.GetPixel(sx, sy); r = px.R; g = px.G; b = px.B; }
            else { var dc = GetDC(IntPtr.Zero); uint c = GetPixel(dc, (int)s.X, (int)s.Y); ReleaseDC(IntPtr.Zero, dc); r = (byte)(c & 0xFF); g = (byte)((c >> 8) & 0xFF); b = (byte)((c >> 16) & 0xFF); }
            _value = Tools2.Colour(r, g, b); _hex.Text = _value; _swatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        }
        // keep the chip beside the pointer and on screen
        double x = p.X + 18, y = p.Y + 22, w = _chip.ActualWidth > 0 ? _chip.ActualWidth : 260, h = _chip.ActualHeight > 0 ? _chip.ActualHeight : 38;
        if (x + w > ActualWidth - 8) x = p.X - w - 18; if (y + h > ActualHeight - 8) y = p.Y - h - 18;
        Canvas.SetLeft(_chip, Math.Max(8, x)); Canvas.SetTop(_chip, Math.Max(8, y));
    }
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
}

// ───────────────────────── Background watchers ─────────────────────────
// System alerts (busy processor, full disk, low battery), finished downloads, and clipboard housekeeping.
public static class Watch
{
    static int _n; static readonly Queue<double> _cpu = new();
    static readonly Dictionary<string, DateTime> _said = new();
    static FileSystemWatcher? _downloads; static DateTime _clipAt = DateTime.MaxValue; static bool _selfClip;

    static bool Once(string key, int minutes)
    {
        if (_said.TryGetValue(key, out var at) && DateTime.Now - at < TimeSpan.FromMinutes(minutes)) return false;
        _said[key] = DateTime.Now; return true;
    }
    // called once a second from the island
    public static void Tick()
    {
        var s = Settings.Current;
        Remind.Check();
        Housekeeping.Tick();
        if (_n % 120 == 0) Svc.Clip.Trim();
        if (s.ClipAutoClearMinutes > 0 && _clipAt != DateTime.MaxValue && DateTime.Now - _clipAt > TimeSpan.FromMinutes(s.ClipAutoClearMinutes))
        {
            _clipAt = DateTime.MaxValue;
            try { _selfClip = true; Clipboard.Clear(); } catch { } finally { _selfClip = false; }
        }
        if (++_n % 5 != 0 || !s.SystemAlerts) return;
        try
        {
            Svc.Stats.Sample();
            _cpu.Enqueue(Svc.Stats.Cpu); while (_cpu.Count > 12) _cpu.Dequeue();
            if (!s.AlertCpu && _cpu.Count == 12 && _cpu.All(v => v > 0.9) && Once("cpu", 30))
                Svc.Notices.Add("Processor has been busy for a minute", $"Load is at {Svc.Stats.Cpu * 100:0}%.", "\uE950");
            if (_n % 60 == 0)
            {
                var sys = new DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
                double free = (double)sys.AvailableFreeSpace / sys.TotalSize;
                if (!s.AlertDisk && free < 0.08 && Once("disk", 240))
                    Svc.Notices.Add("Disk is nearly full", $"{sys.Name} has {sys.AvailableFreeSpace / 1073741824.0:0.0} GB left.", "\uEDA2");
                var p = System.Windows.Forms.SystemInformation.PowerStatus;
                if (p.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery && p.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline && p.BatteryLifePercent < 0.2 && Once("battery", 20))
                    Svc.Notices.Add("Battery is low", $"{p.BatteryLifePercent * 100:0}% left.", "\uE850");
            }
        }
        catch { }
    }

    public static void ClipboardChanged()
    {
        if (_selfClip) return;
        _clipAt = DateTime.Now;
        if (Settings.Current.CleanLinks) Tools.CleanClipboardLink(false);
    }

    static readonly string[] Partial = { ".crdownload", ".part", ".partial", ".download", ".tmp", ".opdownload" };
    public static void Restart()
    {
        _downloads?.Dispose(); _downloads = null;
        if (!Settings.Current.DownloadNotices) return;
        try
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(dir)) return;
            _downloads = new FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.FileName, EnableRaisingEvents = true };
            // browsers write to a temporary name and rename it when the download is complete
            _downloads.Renamed += (_, e) =>
            {
                string was = System.IO.Path.GetExtension(e.OldName ?? "").ToLowerInvariant(), now = System.IO.Path.GetExtension(e.Name ?? "").ToLowerInvariant();
                if (!Partial.Contains(was) || Partial.Contains(now)) return;
                Application.Current?.Dispatcher.BeginInvoke(() => Svc.Notices.Add("Download finished", System.IO.Path.GetFileName(e.FullPath), "\uE896"));
            };
        }
        catch { }
    }
}

// ───────────────────────── Reminders ─────────────────────────
// A reminder goes off once at its time: a notice on the pill and, if allowed, the island opens on the Reminders
// tab (even over a fullscreen app) with Snooze and Done. Daily and weekly ones schedule themselves again on Done.
public static class Remind
{
    public static event Action? Changed;
    static List<Reminder> All => Settings.Current.Reminders;
    public static bool AnyDue => All.Any(r => r.Fired);

    public static void Add(string text, DateTime at, string repeat)
    {
        All.Add(new Reminder { Text = text.Trim(), At = at, Repeat = repeat });
        Settings.Current.Save(); Changed?.Invoke();
    }
    public static void Snooze(Reminder r, int minutes = 10) { r.At = DateTime.Now.AddMinutes(minutes); r.Fired = false; Settings.Current.Save(); Changed?.Invoke(); Settle(); }
    public static void Done(Reminder r)
    {
        if (r.Repeat != "none")
        {
            var next = r.At; var step = r.Repeat == "weekly" ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1);
            do next += step; while (next <= DateTime.Now); // a reminder missed for days resumes at its next slot, not in the past
            r.At = next; r.Fired = false;
        }
        else All.Remove(r);
        Settings.Current.Save(); Changed?.Invoke(); Settle();
    }
    public static void Delete(Reminder r) { All.Remove(r); Settings.Current.Save(); Changed?.Invoke(); Settle(); }

    // once nothing is waiting, the island that opened for a reminder goes away again
    static void Settle() { if (!AnyDue) IslandWindow.Instance.ReleaseReminders(); }

    static DateTime _chimeEnd = DateTime.MinValue, _chimeNext = DateTime.MinValue;
    // called once a second
    public static void Check()
    {
        if (Settings.Current.ReminderSound == "until" && AnyDue && DateTime.Now >= _chimeNext && DateTime.Now <= _chimeEnd)
        { System.Media.SystemSounds.Asterisk.Play(); _chimeNext = DateTime.Now.AddSeconds(15); } // every 15 seconds for up to a minute, until you answer
        foreach (var r in All.Where(r => !r.Fired && r.At <= DateTime.Now).ToList())
        {
            r.Fired = true; Settings.Current.Save();
            if (Settings.Current.ReminderSound != "off") { System.Media.SystemSounds.Asterisk.Play(); _chimeEnd = DateTime.Now.AddSeconds(60); _chimeNext = DateTime.Now.AddSeconds(15); }
            Svc.Notices.Add("Reminder", r.Text, "\uE787");
            if (Settings.Current.ReminderPopups) IslandWindow.Instance.ShowReminders();
            Changed?.Invoke();
        }
    }
    public static string When(DateTime at, string repeat)
    {
        var d = at.Date - DateTime.Today;
        string day = d.Days == 0 ? "Today" : d.Days == 1 ? "Tomorrow" : at.ToString("ddd d MMM");
        string rep = repeat == "daily" ? "  \u00B7  every day" : repeat == "weekly" ? "  \u00B7  every week" : "";
        return $"{day} {UI.TimeText(at)}{rep}";
    }
}

public class RemindersTab : TabBase
{
    public override string Id => "reminders"; public override string Name => "Reminders"; public override string Glyph => "\uE787";
    public override double Height => 262;
    TextBox _text = null!; TextBlock _hint = null!; StackPanel _list = new(); Border _repeat = null!; TextBlock _repeatLabel = null!;
    string _repeatMode = "none"; int _n;
    static readonly string[] Modes = { "none", "daily", "weekly" };

    protected override FrameworkElement Build()
    {
        _text = new TextBox { Background = Brushes.Transparent, Foreground = Brushes.White, CaretBrush = Brushes.White, BorderThickness = new Thickness(0), FontFamily = Font, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        _hint = T("What should I remind you about?", 12.5, Dim); _hint.IsHitTestVisible = false;
        _text.TextChanged += (_, _) => _hint.Visibility = _text.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var field = new Grid(); field.Children.Add(_hint); field.Children.Add(_text);
        var box = new Border { CornerRadius = new CornerRadius(10), BorderBrush = Glass.CardRim, BorderThickness = new Thickness(1), Background = B("#14FFFFFF"), Padding = new Thickness(11, 7, 11, 7), Child = field };
        box.MouseLeftButtonDown += (_, e) => { Window.GetWindow(_text)?.Activate(); _text.Focus(); Keyboard.Focus(_text); e.Handled = true; };

        var when = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        void Chip(string label, Func<DateTime> at, string tip)
        {
            var c = Btn(T(label, 10.5, "#FFFFFF", true), () => Schedule(at()), Card, 9, new Thickness(9, 5, 9, 5));
            c.Margin = new Thickness(0, 0, 5, 5); c.ToolTip = tip; when.Children.Add(c);
        }
        Chip("In 10 min", () => DateTime.Now.AddMinutes(10), "Remind me in ten minutes");
        Chip("In 1 hour", () => DateTime.Now.AddHours(1), "Remind me in an hour");
        Chip("This evening", () => { var t = DateTime.Today.AddHours(18); return t > DateTime.Now.AddMinutes(1) ? t : DateTime.Now.AddHours(2); }, "Today at 18:00 (or in two hours if that has passed)");
        Chip("Tomorrow 9:00", () => DateTime.Today.AddDays(1).AddHours(9), "Tomorrow morning at 9");
        _repeatLabel = T("Once", 10.5, "#C7C7CC", true);
        _repeat = Btn(Row(Ico("\uE8EE", 10, "#C7C7CC"), _repeatLabel.M(6)), () => { _repeatMode = Modes[(Array.IndexOf(Modes, _repeatMode) + 1) % Modes.Length]; _repeatLabel.Text = _repeatMode == "none" ? "Once" : _repeatMode == "daily" ? "Every day" : "Every week"; }, "#00FFFFFF", 9, new Thickness(8, 5, 9, 5));
        _repeat.Margin = new Thickness(0, 0, 0, 5); _repeat.ToolTip = "How often it repeats"; when.Children.Add(_repeat);

        var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        scroll.PreviewMouseWheel += (_, e) => { scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta * 0.6); e.Handled = true; };
        Remind.Changed += Fill;
        Fill();
        var g = MakeGrid("*", "auto,auto,*"); g.Children.Add(box.At(0, 0)); g.Children.Add(when.At(1, 0)); g.Children.Add(scroll.At(2, 0));
        g.Margin = new Thickness(14, 4, 14, 12);
        return g;
    }
    void Schedule(DateTime at)
    {
        if (_text.Text.Trim().Length == 0) { _hint.Text = "Type what to remind you about first"; _text.Focus(); return; }
        Remind.Add(_text.Text, at, _repeatMode);
        _text.Clear(); _hint.Text = "What should I remind you about?";
    }
    public static bool WantFocus;
    public override void Shown()
    {
        Fill();
        if (WantFocus) { WantFocus = false; _text.Dispatcher.BeginInvoke(() => { Window.GetWindow(_text)?.Activate(); _text.Focus(); Keyboard.Focus(_text); }); }
    }
    public override void Tick() { if (++_n % 15 == 0) Fill(); }

    void Fill()
    {
        _list.Children.Clear();
        var all = Settings.Current.Reminders.OrderBy(r => r.Fired ? 0 : 1).ThenBy(r => r.At).ToList();
        if (all.Count == 0)
        {
            var none = T("Nothing waiting. Reminders you add show up here, and again when they go off.", 11, Dim); none.TextWrapping = TextWrapping.Wrap; none.Margin = new Thickness(2, 6, 2, 0);
            _list.Children.Add(none); return;
        }
        foreach (var r in all)
        {
            var rem = r;
            var line = T(rem.Text, 12, "#FFFFFF", true); line.TextTrimming = TextTrimming.CharacterEllipsis;
            var sub = T(rem.Fired ? "Now  \u00B7  " + Remind.When(rem.At, rem.Repeat) : Remind.When(rem.At, rem.Repeat), 10.5, rem.Fired ? "#FFD60A" : Dim);
            var left = Col(line, sub);
            var row = MakeGrid("*,auto", "auto"); row.Children.Add(left.At(0, 0));
            if (rem.Fired)
            {
                var actions = Row(Pill("Snooze", () => Remind.Snooze(rem)), Pill("Done", () => Remind.Done(rem), true));
                foreach (var el in actions.Children.OfType<FrameworkElement>()) el.Margin = new Thickness(5, 0, 0, 0);
                row.Children.Add(actions.At(0, 1));
            }
            else
            {
                var del = IconBtn("\uE711", () => Remind.Delete(rem), 9, "#8E8E93", 26); del.ToolTip = "Delete";
                row.Children.Add(del.At(0, 1));
            }
            var card = Box(row, Card, 12, new Thickness(11, 7, 7, 7)); card.Margin = new Thickness(0, 0, 0, 6);
            _list.Children.Add(card);
        }
    }
}
