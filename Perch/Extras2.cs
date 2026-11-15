using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using static Perch.UI;

namespace Perch;

public partial class Settings
{
    public string ColourFormat { get; set; } = "hex";           // hex | rgb | hsl | css | csharp
    public bool ClipSkipSensitive { get; set; }                 // text that looks like a password or a key never reaches the history
    public bool ClipClearOnLock { get; set; }                   // the system clipboard is emptied when the screen locks
    public bool ClipClearOnSleep { get; set; }                  // and when the PC sleeps
    public bool MusicBlock { get; set; }                        // a music app that starts right after a play key is closed again
    public List<string> MusicBlockApps { get; set; } = new() { "Spotify.exe", "iTunes.exe", "AppleMusic.exe", "Microsoft.Media.Player.exe", "Music.UI.exe" };
    public List<string> QuitKept { get; set; } = new();         // programs "quit all" leaves running
    public string SnippetFolderFilter { get; set; } = "";
}

public static class Tools2
{
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint f, uint t, out IntPtr r);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("shell32.dll")] static extern void SHChangeNotify(int ev, uint flags, IntPtr a, IntPtr b);

    // ── colour formats ──
    public static string Colour(int r, int g, int b)
    {
        switch (Settings.Current.ColourFormat)
        {
            case "rgb": return $"rgb({r}, {g}, {b})";
            case "css": return $"#{r:x2}{g:x2}{b:x2}";
            case "csharp": return $"Color.FromRgb({r}, {g}, {b})";
            case "hsl":
                {
                    double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0, mx = Math.Max(rf, Math.Max(gf, bf)), mn = Math.Min(rf, Math.Min(gf, bf)), l = (mx + mn) / 2, h = 0, s = 0, d = mx - mn;
                    if (d > 0)
                    {
                        s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
                        h = mx == rf ? (gf - bf) / d + (gf < bf ? 6 : 0) : mx == gf ? (bf - rf) / d + 2 : (rf - gf) / d + 4; h *= 60;
                    }
                    return $"hsl({h:0}, {s * 100:0}%, {l * 100:0}%)";
                }
            default: return $"#{r:X2}{g:X2}{b:X2}";
        }
    }

    // Reads the words in a picture and copies them. A code in the picture is copied instead.
    public static async System.Threading.Tasks.Task ReadImageText(BitmapSource img)
    {
        try
        {
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(img)); using var ms = new System.IO.MemoryStream(); enc.Save(ms); ms.Position = 0;
            using (var bmp = new System.Drawing.Bitmap(ms)) { var code = Qr.Decode(bmp); if (code != null) { Clipboard.SetText(code); Svc.Notices.Add("Code copied", code.Length > 60 ? code[..59] + "…" : code, ""); return; } }
            ms.Position = 0;
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
            using var soft = await decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
            var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) { Svc.Notices.Add("Text reading is not available", "Windows has no reading language installed.", ""); return; }
            var result = await engine.RecognizeAsync(soft);
            string text = string.Join(Environment.NewLine, result.Lines.Select(l => l.Text)).Trim();
            if (text.Length == 0) { Svc.Notices.Add("No text found", "", ""); return; }
            Clipboard.SetText(text); Svc.Notices.Add("Text copied", text.Replace(Environment.NewLine, " ").Length > 60 ? text.Replace(Environment.NewLine, " ")[..59] + "…" : text.Replace(Environment.NewLine, " "), "");
        }
        catch (Exception e) { Svc.Notices.Add("Could not read the text", e.Message.Length > 70 ? e.Message[..70] : e.Message, ""); }
    }

    // ── system toggles ──
    static void Advanced(string name, int value)
    {
        try { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true); k?.SetValue(name, value, Microsoft.Win32.RegistryValueKind.DWord); } catch { }
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, IntPtr.Zero, 2, 300, out _);
    }
    static int Adv(string name, int dflt) { try { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"); return k?.GetValue(name) is int v ? v : dflt; } catch { return dflt; } }
    public static bool HiddenShown => Adv("Hidden", 2) == 1;
    public static bool ExtensionsShown => Adv("HideFileExt", 1) == 0;
    public static void ToggleHidden() { bool on = !HiddenShown; Advanced("Hidden", on ? 1 : 2); Svc.Notices.Add(on ? "Hidden files shown" : "Hidden files hidden", "Press F5 in an open Explorer window.", ""); }
    public static void ToggleExtensions() { bool on = !ExtensionsShown; Advanced("HideFileExt", on ? 0 : 1); Svc.Notices.Add(on ? "File extensions shown" : "File extensions hidden", "Press F5 in an open Explorer window.", ""); }
    public static void DisplayOff() => System.Threading.Tasks.Task.Delay(700).ContinueWith(_ => PostMessage((IntPtr)0xFFFF, 0x0112, (IntPtr)0xF170, (IntPtr)2));
    public static void ScreenSaver() => PostMessage((IntPtr)0xFFFF, 0x0112, (IntPtr)0xF140, IntPtr.Zero);

    // Ask every program that has a window to close, except the one in front, Perch, the shell and the programs on the kept list.
    public static void QuitOthers(bool includeFront)
    {
        var front = WinList.PathOf(ForegroundPid()); var keep = Settings.Current.QuitKept;
        var targets = WinList.List(true).Where(w => !w.Exe.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) && !keep.Any(k => k.Equals(w.Exe, StringComparison.OrdinalIgnoreCase)) && (includeFront || !w.Path.Equals(front, StringComparison.OrdinalIgnoreCase))).ToList();
        if (targets.Count == 0) { Svc.Notices.Add("Nothing else to close", "", ""); return; }
        var names = string.Join("\n", targets.Select(t => t.Exe).Distinct().Take(14).Select(n => "• " + n));
        IslandWindow.Instance.Hold(true);
        bool ok = MessageBox.Show($"Ask {targets.Select(t => t.Exe).Distinct().Count()} programs to close?\n\n{names}\n\nEach one gets the usual close request, so it can ask you to save first.", "Perch", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        IslandWindow.Instance.Hold(false);
        if (!ok) return;
        foreach (var t in targets) WinList.Close(t.Handle);
    }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    static uint ForegroundPid() { GetWindowThreadProcessId(GetForegroundWindow(), out uint pid); return pid; }

    // ── sensitive text and clipboard clearing ──
    static readonly Regex TokenLike = new(@"^(sk-[A-Za-z0-9_\-]{16,}|ghp_[A-Za-z0-9]{20,}|github_pat_\w+|xox[abp]-[\w\-]+|AKIA[0-9A-Z]{12,}|eyJ[\w\-]+\.[\w\-]+\.[\w\-]+|-----BEGIN [A-Z ]*PRIVATE KEY)", RegexOptions.Compiled);
    public static bool LooksSensitive(string t)
    {
        t = t.Trim(); if (t.Length < 8 || t.Length > 200) return false;
        if (TokenLike.IsMatch(t)) return true;
        if (t.Contains(' ') || t.Contains('\n')) return false;
        int kinds = (t.Any(char.IsUpper) ? 1 : 0) + (t.Any(char.IsLower) ? 1 : 0) + (t.Any(char.IsDigit) ? 1 : 0) + (t.Any(c => !char.IsLetterOrDigit(c)) ? 1 : 0);
        return kinds >= 4 && t.Length >= 10 && !t.Contains("://") && !t.Contains('@') && !t.Contains('\\') && !t.Contains('/');
    }
    public static bool SourceSaysPrivate()
    {
        try { var d = Clipboard.GetDataObject(); return d != null && (d.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || d.GetDataPresent("Clipboard Viewer Ignore")); } catch { return false; }
    }
    public static volatile bool Locked; static bool _wired;
    public static void Init()
    {
        if (_wired) return; _wired = true;
        Microsoft.Win32.SystemEvents.SessionSwitch += (_, e) => { if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock) Locked = true; else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) Locked = false; if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionLock && Settings.Current.ClipClearOnLock) Svc.Ui(ClearClipboard); };
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) => { if (e.Mode == Microsoft.Win32.PowerModes.Suspend && Settings.Current.ClipClearOnSleep) Svc.Ui(ClearClipboard); };
    }
    static void ClearClipboard() { try { Clipboard.Clear(); } catch { } }

    // ── music apps that should not wake up ──
    static readonly HashSet<int> _blocked = new();
    public static void MusicTick()
    {
        var s = Settings.Current; if (!s.MusicBlock) return;
        long at = Hooks.MediaAt; if (at == 0 || Environment.TickCount64 - at > 8000) return;
        var since = DateTime.Now.AddMilliseconds(-(Environment.TickCount64 - at) - 1500);
        foreach (var exe in s.MusicBlockApps)
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(exe)))
            {
                try
                {
                    if (p.StartTime < since || _blocked.Contains(p.Id)) continue;
                    _blocked.Add(p.Id);
                    foreach (var w in WinList.List(true).Where(w => w.Pid == (uint)p.Id)) WinList.Close(w.Handle);
                    if (!p.WaitForExit(1500)) p.CloseMainWindow();
                    Svc.Notices.Add("Kept " + System.IO.Path.GetFileNameWithoutExtension(exe) + " from opening", "It started right after a play key.", "");
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        if (_blocked.Count > 40) _blocked.Clear();
    }
}

public static class Qr
{
    // Reads a QR code or bar code out of a picture of the screen. Null when there is none.
    public static string? Decode(System.Drawing.Bitmap bmp)
    {
        try
        {
            var bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bytes = new byte[bd.Stride * bmp.Height]; Marshal.Copy(bd.Scan0, bytes, 0, bytes.Length); bmp.UnlockBits(bd);
            var reader = new ZXing.BarcodeReaderGeneric { AutoRotate = true, Options = new ZXing.Common.DecodingOptions { TryHarder = true } };
            var r = reader.Decode(new ZXing.RGBLuminanceSource(bytes, bmp.Width, bmp.Height, ZXing.RGBLuminanceSource.BitmapFormat.BGRA32));
            return r?.Text;
        }
        catch { return null; }
    }
}

// A small search-and-pick window, used for the snippet menu and the clipboard history window.
public class QuickPick : Window
{
    public sealed record PickItem(string Title, string Sub, Action Run, string Glyph = "");
    static QuickPick? _open;
    readonly TextBox _box; readonly StackPanel _list = new(); readonly Func<string, List<PickItem>> _source; List<PickItem> _items = new(); int _sel; readonly string _footer; readonly bool _digits; GlassBackdrop? _glass;
    public static void Show(string placeholder, Func<string, List<PickItem>> source, string footer, bool digits = false)
    {
        _open?.Close(); _open = new QuickPick(placeholder, source, footer, digits); ((Window)_open).Show(); _open.Activate();
    }
    QuickPick(string placeholder, Func<string, List<PickItem>> source, string footer, bool digits)
    {
        _source = source; _footer = footer; _digits = digits;
        Title = "Perch quick pick"; Width = 520; Height = 430; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; UseLayoutRounding = true;
        var wa = System.Windows.SystemParameters.WorkArea; Left = wa.Left + (wa.Width - Width) / 2; Top = wa.Top + wa.Height * 0.2;
        _box = new TextBox { Background = Brushes.Transparent, Foreground = Brushes.White, CaretBrush = Brushes.White, BorderThickness = new Thickness(0), FontFamily = Display, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
        var hint = T(placeholder, 16, Dim); hint.IsHitTestVisible = false; hint.FontFamily = Display;
        var field = new Grid(); field.Children.Add(hint); field.Children.Add(_box);
        _box.TextChanged += (_, _) => { hint.Visibility = _box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; _sel = 0; Fill(true); };
        var top = MakeGrid("auto,*", "auto"); top.Children.Add(Ico("", 15, Dim).M(0, 0, 12).At(0, 0)); top.Children.Add(field.At(0, 1)); top.Margin = new Thickness(22, 16, 22, 10);
        var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Padding = new Thickness(10, 6, 10, 6) };
        scroll.PreviewMouseWheel += (_, e) => { scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta * 0.8); e.Handled = true; };
        var foot = T(footer, 10.5, Dim); foot.Margin = new Thickness(22, 6, 22, 12);
        var root = MakeGrid("*", "auto,auto,*,auto"); root.Children.Add(top.At(0, 0)); root.Children.Add(new Border { Height = 1, Background = B(Line) }.At(1, 0)); root.Children.Add(scroll.At(2, 0)); root.Children.Add(foot.At(3, 0));
        Content = new Border { Background = Glass.Panel, CornerRadius = new CornerRadius(16), BorderBrush = Glass.On ? Glass.Rim : B("#2C2C2E"), BorderThickness = new Thickness(1), Margin = new Thickness(18), Child = root,
            Effect = Glass.On ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Opacity = 0.5, Color = Colors.Black } };
        SourceInitialized += (_, _) => { _glass = new GlassBackdrop(this) { Reach = 0.75f }; _glass.SetShape(18, 18, Width - 36, Height - 36, 16); _glass.Follow(); };
        Loaded += (_, _) => { Activate(); _box.Focus(); Fill(true); };
        Deactivated += (_, _) => { if (_open == this) Close(); };
        Closed += (_, _) => { if (_open == this) _open = null; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Down) { Move(1); e.Handled = true; }
            else if (e.Key == Key.Up) { Move(-1); e.Handled = true; }
            else if (e.Key == Key.Enter) { Run(_sel); e.Handled = true; }
            else if (_digits && Keyboard.Modifiers == ModifierKeys.Control && e.Key >= Key.D1 && e.Key <= Key.D9) { Run(e.Key - Key.D1); e.Handled = true; }
        };
    }
    void Move(int d) { if (_items.Count == 0) return; _sel = (_sel + d + _items.Count) % _items.Count; Fill(false); }
    void Run(int i) { if (i < 0 || i >= _items.Count) return; var run = _items[i].Run; Close(); try { run(); } catch { } }
    void Fill(bool rebuild)
    {
        if (rebuild) _items = _source(_box.Text.Trim());
        _list.Children.Clear();
        if (_items.Count == 0) { _list.Children.Add(T("Nothing matches.", 11.5, Dim).M(10, 8)); return; }
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i]; int idx = i;
            var row = MakeGrid(_digits ? "22,*" : "30,*", "auto");
            row.Children.Add((_digits ? T(i < 9 ? (i + 1).ToString() : "", 11, Dim, false, true) : Ico(it.Glyph.Length > 0 ? it.Glyph : "", 14, i == _sel ? "#FFFFFF" : "#C7C7CC")).With(x => x.VerticalAlignment = VerticalAlignment.Center).At(0, 0));
            row.Children.Add(Col(T(it.Title, 12.5, "#FFFFFF", true).With(x => x.TextTrimming = TextTrimming.CharacterEllipsis), it.Sub.Length > 0 ? T(it.Sub, 10.5, Dim).With(x => x.TextTrimming = TextTrimming.CharacterEllipsis) : new Border { Height = 0 }).At(0, 1));
            _list.Children.Add(Btn(row, () => Run(idx), i == _sel ? "#33FFFFFF" : "#00FFFFFF", 10, new Thickness(10, 6, 10, 6)));
        }
    }
}

public static class QuickPicks
{
    static IntPtr _prev;
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    static void Paste(string text, IntPtr prev)
    {
        if (prev != IntPtr.Zero) WinList.Activate(prev);
        System.Threading.Tasks.Task.Delay(120).ContinueWith(_ => Hooks.TypeText(text));
    }
    // A searchable menu of snippets; the one you pick is typed where the cursor is.
    public static void Snippets()
    {
        _prev = GetForegroundWindow(); var prev = _prev;
        QuickPick.Show("Search snippets", q =>
        {
            var all = Settings.Current.TextSnippets.Where(t => t.InLibrary && t.Text.Length > 0).ToList();
            return all.Where(t => q.Length == 0 || (t.Name + " " + t.Trigger + " " + t.Folder + " " + t.Text).Contains(q, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.Folder).ThenBy(t => t.Name.Length > 0 ? t.Name : t.Trigger)
                .Select(t => new QuickPick.PickItem((t.Name.Length > 0 ? t.Name : t.Trigger) + (t.Folder.Length > 0 ? "   ·   " + t.Folder : ""), t.Text.Replace("\r", "").Replace("\n", " ").Trim(), () => Paste(Hooks.Fill(t.Text), prev), "")).ToList();
        }, "Enter types it where your cursor is · Esc closes");
    }
    // The clipboard history, with Ctrl+1 to Ctrl+9 to paste one straight away.
    public static void Clipboard_()
    {
        _prev = GetForegroundWindow(); var prev = _prev;
        QuickPick.Show("Search what you copied", q =>
        {
            var list = Svc.Clip.Entries.Where(e => e.Text != null && (q.Length == 0 || e.Text.Contains(q, StringComparison.OrdinalIgnoreCase))).Take(60).ToList();
            return list.Select(e => new QuickPick.PickItem((e.Text!.Replace("\r", "").Replace("\n", " ").Trim() is var s && s.Length > 90 ? s[..90] + "…" : e.Text!.Replace("\r", "").Replace("\n", " ").Trim()), (e.Pinned ? "Pinned · " : "") + e.At.ToString("t"), () =>
            {
                if (e.Files != null) { Svc.Clip.CopyBack(e); Paste("", prev); System.Threading.Tasks.Task.Delay(220).ContinueWith(_ => Hooks.Press(0x11, 0x56)); return; }
                Svc.Clip.CopyBack(e); if (prev != IntPtr.Zero) WinList.Activate(prev);
                System.Threading.Tasks.Task.Delay(160).ContinueWith(_ => Hooks.Press(0x11, 0x56));
            })).ToList();
        }, "Enter pastes it · Ctrl+1 to Ctrl+9 paste that row · Esc closes", true);
    }
}

public static partial class Hooks
{
    public static long MediaAt;
    public static string ListeningMouse => !_extMouseOn() ? "Not listening. No mouse tweak is on." : "Watching mouse buttons and the wheel for the tweaks you switched on" + (_gestureBusy ? ", and movement during a drag." : ".");
    public static string ListeningKeys => !_extKeysOn() ? "Not listening. No keyboard tweak is on." : "Watching keys for the tweaks you switched on. Nothing typed is stored or sent.";
    static bool _extMouseOn() => _thread != null && _runMouse;
    static bool _extKeysOn() => _thread != null && _runKeys;
    // Moves the pointer one pixel and back, so the PC and chat apps see activity.
    public static void Jiggle()
    {
        Send(new List<INPUT> { new INPUT { type = 0, u = new INPUTUNION { mi = new MOUSEINPUT { dx = 1, dy = 0, dwFlags = 1, dwExtraInfo = Magic } } }, new INPUT { type = 0, u = new INPUTUNION { mi = new MOUSEINPUT { dx = -1, dy = 0, dwFlags = 1, dwExtraInfo = Magic } } } });
    }
    // Types text as if from the keyboard, so it lands wherever the cursor is.
    public static void TypeText(string text)
    {
        var l = new List<INPUT>();
        foreach (var c in text.Replace("\r\n", "\n"))
        {
            if (c == '\n') { l.Add(Key(0x0D, false)); l.Add(Key(0x0D, true)); continue; }
            l.Add(Uni(c, false)); l.Add(Uni(c, true));
            if (l.Count > 400) { Send(l); l = new List<INPUT>(); }
        }
        Send(l);
    }
}
