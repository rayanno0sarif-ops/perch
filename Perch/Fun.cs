using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.IO.Path;
using System.Windows.Threading;
using static Perch.UI;

namespace Perch;

public partial class Settings
{
    public string Language { get; set; } = "English";
    public bool MascotOn { get; set; }
    public string MascotShape { get; set; } = "ball";            // ball | egg | pill | square
    public string MascotColour { get; set; } = "#F2F2F7";
    public string MascotSide { get; set; } = "left";
    public bool MascotHidesIdle { get; set; } = true;
    public bool MascotVisits { get; set; } = true;
}

// Other languages: Perch's text is in English; the assistant (your own API key) translates the words the screens use into a file on this PC.
public static class Loc
{
    static Dictionary<string, string> _map = new(); static string _loaded = "English";
    public static readonly HashSet<string> Seen = new();
    public static readonly string[] Languages = { "English", "Español", "Français", "Deutsch", "Italiano", "Português (Brasil)", "日本語", "한국어", "简体中文", "繁體中文", "Русский", "Türkçe", "Українська", "Slovenčina" };
    static string Dir => Path.Combine(Settings.Dir, "lang");
    static string FileFor(string lang) => Path.Combine(Dir, string.Concat(lang.Select(c => char.IsLetterOrDigit(c) ? c : '_')) + ".json");
    public static void Load()
    {
        var lang = Settings.Current.Language; _loaded = lang; _map = new();
        if (lang == "English") return;
        try { var f = FileFor(lang); if (File.Exists(f)) _map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f)) ?? new(); } catch { }
    }
    public static string Tr(string s)
    {
        if (s.Length < 2) return s;
        if (_loaded == "English") { if (Seen.Count < 20000 && s.Any(char.IsLetter)) Seen.Add(s); return s; }
        return _map.TryGetValue(s, out var t) ? t : s;
    }
    public static bool Has => _map.Count > 0;
    public static int Count => _map.Count;
    // The English words every screen uses, collected by building each page and tab once.
    public static async Task<(int Done, string Error)> Translate(IEnumerable<string> strings, string lang, Action<string> progress)
    {
        string key = Secret.Open(Settings.Current.AiKey); if (key.Length == 0) return (0, "Add your API key under Assistant first.");
        var todo = strings.Where(s => s.Length >= 2 && s.Any(char.IsLetter) && !_map.ContainsKey(s)).Distinct().ToList(); var result = new Dictionary<string, string>(_map);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) }; int done = 0;
        for (int i = 0; i < todo.Count; i += 60)
        {
            var batch = todo.Skip(i).Take(60).ToList(); progress($"Translating {Math.Min(i + 60, todo.Count)} of {todo.Count}…");
            var prompt = $"Translate these user interface strings from English into {lang}. Reply with ONLY a JSON object whose keys are the exact English strings and whose values are the translations. Keep key names (Alt, Ctrl, Shift, Win, Esc, Enter), product names (Perch, Windows, Bluetooth), numbers, units, placeholders in braces and any symbols unchanged. Keep each translation about as short as the original.\n\n" + JsonSerializer.Serialize(batch);
            var body = JsonSerializer.Serialize(new { model = Settings.Current.AiModel, max_tokens = 8000, messages = new[] { new { role = "user", content = prompt } } });
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            req.Headers.Add("x-api-key", key); req.Headers.Add("anthropic-version", "2023-06-01");
            using var res = await http.SendAsync(req); var json = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) return (done, $"{(int)res.StatusCode}: the request was refused.");
            try
            {
                using var d = JsonDocument.Parse(json); string text = string.Concat(d.RootElement.GetProperty("content").EnumerateArray().Where(p => p.TryGetProperty("text", out _)).Select(p => p.GetProperty("text").GetString()));
                int a = text.IndexOf('{'), b = text.LastIndexOf('}'); if (a < 0 || b < a) continue;
                var part = JsonSerializer.Deserialize<Dictionary<string, string>>(text[a..(b + 1)]); if (part == null) continue;
                foreach (var kv in part) { result[kv.Key] = kv.Value; done++; }
            }
            catch { }
        }
        Directory.CreateDirectory(Dir); File.WriteAllText(FileFor(lang), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        return (done, "");
    }
    public static void Restart()
    {
        var exe = Environment.ProcessPath!;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & start \"\" \"{exe}\"") { CreateNoWindow = true, UseShellExecute = false });
        Application.Current.Shutdown();
    }
}

// A small friend beside the island. It rests when nothing happens, hops over when something does, and likes being petted.
public class Mascot : Window
{
    static Mascot? _me; readonly Grid _g = new(); readonly Border _body = new(); readonly Ellipse _l = new() { Width = 6, Height = 8, Fill = Brushes.Black }, _r = new() { Width = 6, Height = 8, Fill = Brushes.Black };
    readonly System.Windows.Shapes.Path _mouth = new() { Stroke = Brushes.Black, StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    readonly TranslateTransform _hop = new(); readonly ScaleTransform _sc = new(1, 1); readonly DispatcherTimer _life = new() { Interval = TimeSpan.FromMilliseconds(400) }; DateTime _lastEvent = DateTime.Now; bool _sleeping; int _tick; readonly Random _rng = new();
    public static void Sync()
    {
        if (Settings.Current.MascotOn) { if (_me == null) { _me = new Mascot(); _me.Show(); } else _me.Restyle(); }
        else if (_me != null) { _me.Close(); _me = null; }
    }
    Mascot()
    {
        Title = "Perch companion"; Width = 60; Height = 60; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        _body.Width = 40; _body.Height = 40; _body.HorizontalAlignment = HorizontalAlignment.Center; _body.VerticalAlignment = VerticalAlignment.Center;
        _body.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.45 };
        var eyes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -6, 0, 0) };
        _l.Margin = new Thickness(0, 0, 9, 0); eyes.Children.Add(_l); eyes.Children.Add(_r);
        _mouth.HorizontalAlignment = HorizontalAlignment.Center; _mouth.VerticalAlignment = VerticalAlignment.Center; _mouth.Margin = new Thickness(0, 10, 0, 0);
        _g.Children.Add(_body); _g.Children.Add(eyes); _g.Children.Add(_mouth); _g.RenderTransform = new TransformGroup { Children = { _sc, _hop } }; _g.RenderTransformOrigin = new Point(0.5, 0.5);
        Content = _g; Smile(0.6); Restyle();
        MouseEnter += (_, _) => { Pet(); };
        SourceInitialized += (_, _) => { var h = new System.Windows.Interop.WindowInteropHelper(this).Handle; SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80 | 0x08000000); };
        Svc.Notices.Added += n => Svc.Ui(() => React(n));
        _life.Tick += (_, _) => Life(); _life.Start(); Place();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    void Restyle()
    {
        var s = Settings.Current; Color c; try { c = (Color)ColorConverter.ConvertFromString(s.MascotColour); } catch { c = Colors.WhiteSmoke; }
        _body.Background = new LinearGradientBrush(Color.FromArgb(255, (byte)Math.Min(255, c.R + 18), (byte)Math.Min(255, c.G + 18), (byte)Math.Min(255, c.B + 18)), c, 90);
        (_body.Width, _body.Height, _body.CornerRadius) = s.MascotShape switch { "egg" => (36.0, 44.0, new CornerRadius(20, 20, 17, 17)), "pill" => (46.0, 32.0, new CornerRadius(16)), "square" => (38.0, 38.0, new CornerRadius(12)), _ => (40.0, 40.0, new CornerRadius(20)) };
        Place();
    }
    void Place()
    {
        var wa = SystemParameters.WorkArea; var isl = IslandWindow.Instance; double cx = isl.Left + isl.Width / 2;
        Left = Settings.Current.MascotSide == "right" ? cx + 118 : cx - 118 - Width; Top = Math.Max(0, isl.Top + 2); if (Left < wa.Left) Left = wa.Left;
    }
    void Smile(double k) { _mouth.Data = Geometry.Parse($"M -8,0 Q 0,{(k * 10):0.#} 8,0"); }
    void Blink() { _l.Height = _r.Height = 1.5; Task.Delay(130).ContinueWith(_ => Svc.Ui(() => { if (!_sleeping) _l.Height = _r.Height = 8; })); }
    void Hop()
    {
        var a = new DoubleAnimationUsingKeyFrames(); a.KeyFrames.Add(new EasingDoubleKeyFrame(-14, KeyTime.FromPercent(0.4), new CubicEase { EasingMode = EasingMode.EaseOut })); a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new BounceEase { Bounces = 1, Bounciness = 3, EasingMode = EasingMode.EaseOut })); a.Duration = TimeSpan.FromMilliseconds(520);
        _hop.BeginAnimation(TranslateTransform.YProperty, a);
    }
    void Wake() { _sleeping = false; _l.Height = _r.Height = 8; Smile(0.7); Opacity = 1; _lastEvent = DateTime.Now; }
    void Pet() { Wake(); Smile(1.6); Hop(); Task.Delay(1500).ContinueWith(_ => Svc.Ui(() => Smile(0.7))); }
    void React(Notice n)
    {
        Wake(); string t = (n.Title + " " + n.Body).ToLowerInvariant(); bool bad = t.Contains("fail") || t.Contains("low") || t.Contains("full") || t.Contains("went wrong") || t.Contains("could not");
        _mouth.Data = bad ? Geometry.Parse("M -7,4 Q 0,-4 7,4") : Geometry.Parse("M -9,0 Q 0,12 9,0"); Hop();
        Task.Delay(2200).ContinueWith(_ => Svc.Ui(() => Smile(0.7)));
    }
    void Life()
    {
        _tick++; Place();
        bool idle = (DateTime.Now - _lastEvent).TotalSeconds > 30;
        if (idle && !_sleeping) { _sleeping = true; _l.Height = _r.Height = 1.5; _mouth.Data = Geometry.Parse("M -4,0 L 4,0"); }
        if (_sleeping && Settings.Current.MascotHidesIdle && (DateTime.Now - _lastEvent).TotalSeconds > 60) Opacity = Math.Max(0, Opacity - 0.1);
        if (!_sleeping && _tick % 9 == 0) Blink();
        if (Settings.Current.MascotVisits && _tick % 450 == 0) { Wake(); Hop(); }
        Visibility = IslandWindow.Instance.IsVisible ? Visibility.Visible : Visibility.Collapsed;
    }
}
