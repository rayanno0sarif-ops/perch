using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using static Perch.UI;

namespace Perch;

public partial class Settings
{
    public List<string> SeenTabs { get; set; } = new();
    public List<string> CalendarSources { get; set; } = new();       // .ics files or links
    public bool CalendarWindows { get; set; } = true;                // also ask Windows for its own appointments
    public bool CalendarWeekNumbers { get; set; }
    public bool AgentsFinishAlert { get; set; }                      // say so when a coding agent finishes a long task
    public int AgentsFinishMinutes { get; set; } = 2;
    public bool AgentsClaude { get; set; } = true;
    public bool AgentsCodex { get; set; } = true;
    public double AgentsDailyBudget { get; set; }                    // API value, 0 is off
    public bool LyricsOnline { get; set; }                           // look lyrics up at lrclib.net
    public int LyricsOffsetMs { get; set; }
    public int PomoFocus { get; set; } = 25;
    public int PomoShort { get; set; } = 5;
    public int PomoLong { get; set; } = 15;
    public int PomoEvery { get; set; } = 4;
    public int PomoTotal { get; set; } = 8;
    public bool TimerSound { get; set; } = true;
    public bool AccessoryAlerts { get; set; }                        // warn once when a paired Bluetooth device falls to 20%
}

// ───────────────────────── Timer: countdown, stopwatch, pomodoro ─────────────────────────
public static class Pomodoro
{
    public static string Mode = "timer";          // timer | stopwatch | pomodoro
    public static string Phase = "focus";         // focus | short | long
    public static int Session = 1, TimerMinutes = 10;
    public static bool Running; public static TimeSpan Left = TimeSpan.FromMinutes(10), Total = TimeSpan.FromMinutes(10), Up = TimeSpan.Zero;
    public static event Action? Changed;
    static readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(250) }; static DateTime _last;
    static Pomodoro() { _t.Tick += (_, _) => Step(); }
    public static void Reset()
    {
        Running = false; _t.Stop(); Up = TimeSpan.Zero; var s = Settings.Current;
        Total = Left = Mode == "pomodoro" ? TimeSpan.FromMinutes(Phase == "focus" ? s.PomoFocus : Phase == "short" ? s.PomoShort : s.PomoLong) : TimeSpan.FromMinutes(TimerMinutes);
        if (Mode == "pomodoro" && Phase == "focus" && Session == 0) Session = 1; Changed?.Invoke();
    }
    public static void Toggle() { if (Running) { Running = false; _t.Stop(); } else { if (Mode != "stopwatch" && Left <= TimeSpan.Zero) Reset(); Running = true; _last = DateTime.UtcNow; _t.Start(); } Changed?.Invoke(); }
    static void Step()
    {
        var now = DateTime.UtcNow; var d = now - _last; _last = now;
        if (Mode == "stopwatch") Up += d;
        else
        {
            Left -= d;
            if (Left <= TimeSpan.Zero) { Left = TimeSpan.Zero; Running = false; _t.Stop(); Finish(); }
        }
        Changed?.Invoke();
    }
    static void Finish()
    {
        var s = Settings.Current;
        if (Mode == "pomodoro")
        {
            bool wasFocus = Phase == "focus";
            Svc.Notices.Add(wasFocus ? "Focus session done" : "Break over", wasFocus ? $"Session {Session} of {s.PomoTotal}. Time for a break." : "Ready for the next focus session.", "");
            if (wasFocus) { if (Session >= s.PomoTotal) { Svc.Notices.Add("Pomodoro complete", $"{s.PomoTotal} sessions.", ""); Session = 1; Phase = "focus"; } else Phase = Session % Math.Max(1, s.PomoEvery) == 0 ? "long" : "short"; }
            else { Phase = "focus"; Session++; }
            Reset();
        }
        else Svc.Notices.Add("Time is up", "", "");
        if (s.TimerSound) { try { System.Media.SystemSounds.Asterisk.Play(); } catch { } }
    }
}

public class TimerTab : TabBase
{
    public override string Id => "timer"; public override string Name => "Timer"; public override string Glyph => ""; public override double Height => 232;
    readonly TextBlock _big = T("10:00", 38, "#FFFFFF", true, true); readonly TextBlock _sub = T("", 11.5, Dim); readonly Border _bar = new() { Height = 4, CornerRadius = new CornerRadius(2), Background = B("#FFFFFF"), HorizontalAlignment = HorizontalAlignment.Left };
    readonly StackPanel _row = new() { Orientation = Orientation.Horizontal }; Border _go = null!; readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal };
    protected override FrameworkElement Build()
    {
        Pomodoro.Changed += Paint; Pomodoro.Reset();
        _go = new Border();
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = B("#26FFFFFF"), Child = _bar };
        _big.HorizontalAlignment = HorizontalAlignment.Center; _sub.HorizontalAlignment = HorizontalAlignment.Center;
        var ctl = Row(_go, Pill("Reset", () => Pomodoro.Reset()).M(8)); ctl.HorizontalAlignment = HorizontalAlignment.Center;
        Modes();
        return Col(_row.With(r => r.HorizontalAlignment = HorizontalAlignment.Center), _big.M(0, 12, 0, 0), _sub.M(0, 2, 0, 8), track.M(30, 0, 30, 10), _chips.With(c => c.HorizontalAlignment = HorizontalAlignment.Center), ctl.M(0, 10, 0, 0)).M(14, 8, 14, 0);
    }
    void Modes()
    {
        _row.Children.Clear();
        foreach (var (id, label) in new[] { ("timer", "Timer"), ("stopwatch", "Stopwatch"), ("pomodoro", "Pomodoro") })
        {
            var mid = id; bool on = Pomodoro.Mode == mid;
            _row.Children.Add(Btn(T(label, 11, on ? "#000000" : "#E5E5EA", true), () => { Pomodoro.Mode = mid; Pomodoro.Reset(); Modes(); }, on ? "#FFFFFF" : Card, 9, new Thickness(11, 5, 11, 5)).M(0, 0, 5, 0));
        }
        _chips.Children.Clear();
        if (Pomodoro.Mode == "timer") foreach (var m in new[] { 1, 5, 10, 15, 30, 60 }) { int mm = m; _chips.Children.Add(Btn(T(m + "m", 10.5, "#E5E5EA", true), () => { Pomodoro.TimerMinutes = mm; Pomodoro.Reset(); }, Card, 8, new Thickness(9, 4, 9, 4)).M(0, 0, 4, 0)); }
        else if (Pomodoro.Mode == "pomodoro")
        {
            var s = Settings.Current;
            _chips.Children.Add(T("Focus", 10.5, Dim).M(0, 5, 4, 0)); _chips.Children.Add(Pill("−", () => { s.PomoFocus = Math.Max(5, s.PomoFocus - 5); s.Save(); Pomodoro.Reset(); Modes(); }));
            _chips.Children.Add(T(s.PomoFocus + "m", 11, "#FFFFFF", true).M(6, 4, 6, 0)); _chips.Children.Add(Pill("+", () => { s.PomoFocus = Math.Min(90, s.PomoFocus + 5); s.Save(); Pomodoro.Reset(); Modes(); }));
            _chips.Children.Add(T("Break", 10.5, Dim).M(12, 5, 4, 0)); _chips.Children.Add(Pill("−", () => { s.PomoShort = Math.Max(1, s.PomoShort - 1); s.Save(); Pomodoro.Reset(); Modes(); }));
            _chips.Children.Add(T(s.PomoShort + "m", 11, "#FFFFFF", true).M(6, 4, 6, 0)); _chips.Children.Add(Pill("+", () => { s.PomoShort = Math.Min(30, s.PomoShort + 1); s.Save(); Pomodoro.Reset(); Modes(); }));
        }
        Paint();
    }
    void Paint()
    {
        if (_go == null) return;
        var t = Pomodoro.Mode == "stopwatch" ? Pomodoro.Up : Pomodoro.Left;
        _big.Text = t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
        if (Pomodoro.Mode == "pomodoro") { var s = Settings.Current; _sub.Text = (Pomodoro.Phase == "focus" ? "Focus" : Pomodoro.Phase == "short" ? "Short break" : "Long break") + $" · session {Pomodoro.Session} of {s.PomoTotal}"; }
        else _sub.Text = Pomodoro.Mode == "stopwatch" ? "Counting up" : Pomodoro.Running ? "Counting down" : "Pick a time";
        double frac = Pomodoro.Mode == "stopwatch" ? (Pomodoro.Up.TotalSeconds % 60) / 60 : Pomodoro.Total.TotalSeconds > 0 ? 1 - Pomodoro.Left.TotalSeconds / Pomodoro.Total.TotalSeconds : 0;
        if (_bar.Parent is Border p && p.ActualWidth > 0) _bar.Width = Math.Clamp(frac, 0, 1) * p.ActualWidth; else _bar.Width = Math.Clamp(frac, 0, 1) * 300;
        string label = Pomodoro.Running ? "Pause" : (Pomodoro.Left < Pomodoro.Total && Pomodoro.Mode != "stopwatch") || Pomodoro.Up > TimeSpan.Zero ? "Resume" : "Start";
        _go.Child = Pill(label, () => Pomodoro.Toggle(), true);
    }
    public override void Shown() { Modes(); }
}

// ───────────────────────── Calendar ─────────────────────────
public record CalEvent(DateTime Start, DateTime End, string Title, bool AllDay);
public static class Cal
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static List<CalEvent> _cache = new(); static DateTime _loaded = DateTime.MinValue; static bool _busy;
    public static List<CalEvent> Events => _cache;
    public static async Task Refresh(bool force = false)
    {
        if (_busy || (!force && DateTime.Now - _loaded < TimeSpan.FromMinutes(10))) return; _busy = true;
        try
        {
            var all = new List<CalEvent>(); var from = DateTime.Today.AddDays(-35); var to = DateTime.Today.AddDays(120);
            foreach (var src in Settings.Current.CalendarSources.ToList())
            {
                try { string text = src.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? await Http.GetStringAsync(src) : File.ReadAllText(src); all.AddRange(ParseIcs(text, from, to)); } catch { }
            }
            if (Settings.Current.CalendarWindows) { try { all.AddRange(await Appointments(from, to)); } catch { } }
            _cache = all.OrderBy(e => e.Start).ToList(); _loaded = DateTime.Now;
        }
        finally { _busy = false; }
    }
    static async Task<List<CalEvent>> Appointments(DateTime from, DateTime to)
    {
        var res = new List<CalEvent>();
        var store = await Windows.ApplicationModel.Appointments.AppointmentManager.RequestStoreAsync(Windows.ApplicationModel.Appointments.AppointmentStoreAccessType.AllCalendarsReadOnly);
        if (store == null) return res;
        var list = await store.FindAppointmentsAsync(from, to - from);
        foreach (var a in list) res.Add(new CalEvent(a.StartTime.LocalDateTime, a.StartTime.LocalDateTime + a.Duration, string.IsNullOrWhiteSpace(a.Subject) ? "Untitled event" : a.Subject, a.AllDay));
        return res;
    }
    static DateTime? ParseDt(string v, out bool date)
    {
        date = false; v = v.Trim();
        if (v.Length == 8 && DateTime.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) { date = true; return d; }
        if (DateTime.TryParseExact(v.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return v.EndsWith("Z") ? DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime() : t;
        return null;
    }
    public static List<CalEvent> ParseIcs(string text, DateTime from, DateTime to)
    {
        var res = new List<CalEvent>(); var lines = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n')) { if ((raw.StartsWith(" ") || raw.StartsWith("\t")) && lines.Count > 0) lines[^1] += raw[1..]; else lines.Add(raw); }
        Dictionary<string, string>? ev = null;
        foreach (var l in lines)
        {
            if (l == "BEGIN:VEVENT") { ev = new(); continue; }
            if (l == "END:VEVENT" && ev != null)
            {
                if (ev.TryGetValue("DTSTART", out var ds) && ParseDt(ds, out bool allDay) is DateTime st)
                {
                    DateTime en = ev.TryGetValue("DTEND", out var de) && ParseDt(de, out _) is DateTime e2 ? e2 : st.AddHours(allDay ? 24 : 1);
                    string title = (ev.GetValueOrDefault("SUMMARY") ?? "Untitled event").Replace("\\,", ",").Replace("\\n", " ").Replace("\\;", ";");
                    ev.TryGetValue("RRULE", out var rr);
                    foreach (var occ in Expand(st, rr, from, to)) res.Add(new CalEvent(occ, occ + (en - st), title, allDay));
                }
                ev = null; continue;
            }
            if (ev == null) continue;
            int c = l.IndexOf(':'); if (c <= 0) continue; string key = l[..c].Split(';')[0], val = l[(c + 1)..]; ev[key] = val;
        }
        return res;
    }
    static IEnumerable<DateTime> Expand(DateTime st, string? rrule, DateTime from, DateTime to)
    {
        if (string.IsNullOrEmpty(rrule)) { if (st >= from && st <= to) yield return st; yield break; }
        var p = rrule.Split(';').Select(x => x.Split('=')).Where(x => x.Length == 2).ToDictionary(x => x[0], x => x[1]);
        string f = p.GetValueOrDefault("FREQ", ""); int iv = int.TryParse(p.GetValueOrDefault("INTERVAL", "1"), out var i1) ? Math.Max(1, i1) : 1; int count = int.TryParse(p.GetValueOrDefault("COUNT", "0"), out var c1) ? c1 : 0;
        DateTime until = p.TryGetValue("UNTIL", out var u) && ParseDt(u, out _) is DateTime ud ? ud : DateTime.MaxValue; int n = 0; var cur = st;
        while (cur <= to && cur <= until && n < 2000)
        {
            if (cur >= from) yield return cur; n++; if (count > 0 && n >= count) yield break;
            cur = f switch { "DAILY" => cur.AddDays(iv), "WEEKLY" => cur.AddDays(7 * iv), "MONTHLY" => cur.AddMonths(iv), "YEARLY" => cur.AddYears(iv), _ => DateTime.MaxValue };
            if (cur == DateTime.MaxValue) yield break;
        }
    }
}

public class CalendarTab : TabBase
{
    public override string Id => "calendar"; public override string Name => "Calendar"; public override string Glyph => ""; public override double Height => 292;
    DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1); DateTime _day = DateTime.Today; readonly StackPanel _body = new();
    protected override FrameworkElement Build() { _ = Cal.Refresh().ContinueWith(_ => Svc.Ui(Paint)); Paint(); return new Border { Child = _body, Margin = new Thickness(14, 4, 14, 12) }; }
    public override void Shown() { _ = Cal.Refresh().ContinueWith(_ => Svc.Ui(Paint)); Paint(); }
    void Paint()
    {
        _body.Children.Clear(); var ev = Cal.Events;
        var head = MakeGrid("auto,*,auto,auto", "auto");
        head.Children.Add(IconBtn("", () => { _month = _month.AddMonths(-1); Paint(); }, 11, "#D1D1D6", 26).At(0, 0));
        head.Children.Add(T(_month.ToString("MMMM yyyy"), 13, "#FFFFFF", true).With(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; }).At(0, 1));
        head.Children.Add(IconBtn("", () => { _month = _month.AddMonths(1); Paint(); }, 11, "#D1D1D6", 26).At(0, 2));
        head.Children.Add(Pill("Today", () => { _month = new(DateTime.Today.Year, DateTime.Today.Month, 1); _day = DateTime.Today; Paint(); }).M(6).At(0, 3));
        _body.Children.Add(head);
        var g = MakeGrid("*,*,*,*,*,*,*", "auto,auto,auto,auto,auto,auto,auto"); g.Margin = new Thickness(0, 6, 0, 6);
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        for (int i = 0; i < 7; i++) g.Children.Add(T(CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)(((int)first + i) % 7))[..Math.Min(2, CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)(((int)first + i) % 7)).Length)], 9.5, Dim, true).With(t => t.HorizontalAlignment = HorizontalAlignment.Center).At(0, i));
        int offset = ((int)_month.DayOfWeek - (int)first + 7) % 7;
        for (int d = 1; d <= DateTime.DaysInMonth(_month.Year, _month.Month); d++)
        {
            var date = new DateTime(_month.Year, _month.Month, d); int cell = offset + d - 1; bool today = date == DateTime.Today, sel = date == _day.Date; bool has = ev.Any(e => e.Start.Date <= date && (e.AllDay ? e.End.Date > date : e.End.Date >= date) && !(e.AllDay && e.End.Date <= date));
            var num = T(d.ToString(), 11, sel ? "#000000" : "#FFFFFF", today || sel).With(t => t.HorizontalAlignment = HorizontalAlignment.Center);
            var dot = new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Background = sel ? Brushes.Black : B("#0A84FF"), HorizontalAlignment = HorizontalAlignment.Center, Visibility = has ? Visibility.Visible : Visibility.Hidden, Margin = new Thickness(0, 1, 0, 0) };
            var b = Btn(Col(num, dot), () => { _day = date; Paint(); }, sel ? "#FFFFFF" : today ? "#33FFFFFF" : "#00FFFFFF", 8, new Thickness(0, 3, 0, 3)); b.Margin = new Thickness(1);
            g.Children.Add(b.At(1 + cell / 7, cell % 7));
        }
        _body.Children.Add(g);
        var list = ev.Where(e => e.Start.Date <= _day.Date && e.End > _day.Date && !(e.AllDay && e.End.Date <= _day.Date)).OrderBy(e => e.Start).Take(4).ToList();
        _body.Children.Add(T(_day.Date == DateTime.Today ? "Today" : _day.ToString("dddd d MMMM"), 10.5, Dim, true).M(2, 0, 0, 4));
        if (list.Count == 0) _body.Children.Add(T(Settings.Current.CalendarSources.Count == 0 && !Settings.Current.CalendarWindows ? "Add a calendar in Settings, under More tabs." : "No appointments.", 11.5, Dim).M(2, 0, 0, 0));
        foreach (var e in list) _body.Children.Add(Row(T(e.AllDay ? "All day" : e.Start.ToString("t"), 11, "#0A84FF", true, true).With(t => t.Width = 70), T(e.Title, 12, "#FFFFFF").With(t => t.TextTrimming = TextTrimming.CharacterEllipsis)));
    }
}

// ───────────────────────── Lyrics ─────────────────────────
public class LyricsTab : TabBase
{
    public override string Id => "lyrics"; public override string Name => "Lyrics"; public override string Glyph => ""; public override double Height => 232;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    readonly StackPanel _lines = new(); readonly TextBlock _msg = T("", 11.5, Dim); string _key = ""; List<(TimeSpan At, string Text)> _synced = new(); string _plain = ""; int _cur = -1; ScrollViewer _scroll = null!;
    protected override FrameworkElement Build()
    {
        _scroll = new ScrollViewer { Content = Col(_msg.M(0, 0, 0, 6), _lines), VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        _scroll.PreviewMouseWheel += (_, e) => { _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset - e.Delta * 0.6); e.Handled = true; };
        var offs = Row(Pill("Earlier", () => { Settings.Current.LyricsOffsetMs -= 500; Settings.Current.Save(); }), Pill("Later", () => { Settings.Current.LyricsOffsetMs += 500; Settings.Current.Save(); }).M(5), Pill("Import…", ImportLrc).M(5));
        return Col(_scroll.With(s => s.Height = 150), offs.M(0, 8, 0, 0)).M(18, 8, 18, 0);
    }
    public override void Shown() { _ = Load(); }
    public override void Tick() { Highlight(); if (Svc.Media.Title + Svc.Media.Artist != _key) _ = Load(); }
    void ImportLrc()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Lyrics|*.lrc;*.txt" }; if (dlg.ShowDialog() != true) return;
        _synced = ParseLrc(File.ReadAllText(dlg.FileName)); _plain = ""; _key = Svc.Media.Title + Svc.Media.Artist; Paint();
    }
    static List<(TimeSpan, string)> ParseLrc(string lrc)
    {
        var res = new List<(TimeSpan, string)>();
        foreach (Match m in Regex.Matches(lrc, @"\[(\d+):(\d+(?:\.\d+)?)\]([^\r\n]*)")) res.Add((TimeSpan.FromSeconds(int.Parse(m.Groups[1].Value) * 60 + double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)), m.Groups[3].Value.Trim()));
        return res.OrderBy(x => x.Item1).ToList();
    }
    async Task Load()
    {
        var m = Svc.Media; _key = m.Title + m.Artist; _synced = new(); _plain = ""; _cur = -1;
        if (!m.HasSession || m.Title.Length == 0) { _msg.Text = "Play a song to see its lyrics."; _lines.Children.Clear(); return; }
        if (!Settings.Current.LyricsOnline) { _msg.Text = "Finding lyrics sends the song, artist and length to lrclib.net. Turn it on in Settings, under More tabs, or import a .lrc file."; _lines.Children.Clear(); return; }
        _msg.Text = "Loading lyrics…"; string key = _key;
        try
        {
            var url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(m.Title)}&artist_name={Uri.EscapeDataString(m.Artist)}" + (m.Duration.TotalSeconds > 0 ? $"&duration={(int)m.Duration.TotalSeconds}" : "");
            var json = await Http.GetStringAsync(url); if (key != _key) return;
            using var doc = JsonDocument.Parse(json); var r = doc.RootElement;
            if (r.TryGetProperty("instrumental", out var ins) && ins.ValueKind == JsonValueKind.True) { _msg.Text = "Instrumental"; _lines.Children.Clear(); return; }
            if (r.TryGetProperty("syncedLyrics", out var sl) && sl.ValueKind == JsonValueKind.String) _synced = ParseLrc(sl.GetString() ?? "");
            if (_synced.Count == 0 && r.TryGetProperty("plainLyrics", out var pl) && pl.ValueKind == JsonValueKind.String) _plain = pl.GetString() ?? "";
        }
        catch { if (key == _key) { _msg.Text = "No matching lyrics for this recording."; _lines.Children.Clear(); } return; }
        Paint();
    }
    void Paint()
    {
        _lines.Children.Clear();
        if (_synced.Count == 0 && _plain.Length == 0) { _msg.Text = "No matching lyrics for this recording."; return; }
        _msg.Text = Svc.Media.Title + "  ·  " + Svc.Media.Artist;
        if (_synced.Count > 0) foreach (var (_, text) in _synced) _lines.Children.Add(T(text.Length == 0 ? "♪" : text, 14, "#8E8E93", true).With(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 3, 0, 3); }));
        else foreach (var l in _plain.Split('\n')) _lines.Children.Add(T(l, 13, "#E5E5EA").With(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 2, 0, 2); }));
        _cur = -1; Highlight();
    }
    void Highlight()
    {
        if (_synced.Count == 0) return;
        var pos = Svc.Media.Position + TimeSpan.FromMilliseconds(Settings.Current.LyricsOffsetMs); int at = -1;
        for (int i = 0; i < _synced.Count; i++) if (_synced[i].At <= pos) at = i; else break;
        if (at == _cur) return; _cur = at;
        for (int i = 0; i < _lines.Children.Count; i++) { var t = (TextBlock)_lines.Children[i]; t.Foreground = B(i == at ? "#FFFFFF" : "#8E8E93"); t.FontSize = i == at ? 17 : 14; }
        if (at >= 0 && _lines.Children[at] is FrameworkElement fe) { var y = fe.TransformToAncestor(_lines).Transform(new Point(0, 0)).Y; _scroll.ScrollToVerticalOffset(Math.Max(0, y - 50)); }
    }
}

// ───────────────────────── Watch: read a part of a window and say when it changes ─────────────────────────
public class WatchTab : TabBase
{
    public override string Id => "watch"; public override string Name => "Watch"; public override string Glyph => ""; public override double Height => 250;
    static System.Drawing.Rectangle? _area; static string _cond = "changes", _text = "", _since = "", _status = "Choose any part of a window, like a progress bar or a build status. Perch reads it and tells you when it changes."; static byte[]? _sig; static DateTime _changedAt; static bool _changed;
    static readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromSeconds(2) }; static bool _busy; static TextBlock? _statusBlock;
    static WatchTab() { _t.Tick += async (_, _) => await Poll(); }
    readonly Border _host = new(); static WatchTab? _me; static void Refresh() => _me?.Fill();
    protected override FrameworkElement Build() { _me = this; Fill(); return _host; }
    void Fill()
    {
        _statusBlock = T(_status, 11.5, Dim).With(t => t.TextWrapping = TextWrapping.Wrap);
        var conds = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (id, label) in new[] { ("changes", "When it changes"), ("settles", "When it stops changing"), ("contains", "When it shows"), ("reaches", "When the number reaches") })
        {
            var cid = id; bool on = _cond == cid;
            conds.Children.Add(Btn(T(label, 10.5, on ? "#000000" : "#E5E5EA", true), () => { _cond = cid; Refresh(); }, on ? "#FFFFFF" : Card, 8, new Thickness(9, 4, 9, 4)).M(0, 0, 4, 0));
        }
        var tb = new TextBox { Text = _text, Background = Brushes.Transparent, Foreground = Brushes.White, CaretBrush = Brushes.White, BorderThickness = new Thickness(0), FontFamily = Font, FontSize = 12, Width = 180 };
        tb.TextChanged += (_, _) => _text = tb.Text;
        var field = Box(tb, Card, 9, new Thickness(10, 5, 10, 5));
        var buttons = Row(Pill(_area == null ? "Choose area" : "Choose another area", ChooseArea, true), Pill(_t.IsEnabled ? "Stop watching" : "Watch", Toggle).M(6));
        _host.Child = Col(_statusBlock, conds.M(0, 10, 0, 6), field.M(0, 0, 0, 8).With(f => f.HorizontalAlignment = HorizontalAlignment.Left), buttons).M(16, 8, 16, 0);
    }
    void ChooseArea()
    {
        IslandWindow.Instance.Vanish(true);
        System.Threading.Tasks.Task.Delay(180).ContinueWith(_ => Svc.Ui(() =>
        {
            var vs = System.Windows.Forms.SystemInformation.VirtualScreen; var frozen = Shot.Grab(vs, false);
            new ShotSelect(frozen, vs, false, r => { _area = r; _sig = null; _status = "Area chosen. Press Watch."; IslandWindow.Instance.Vanish(false); Refresh(); }).Show();
            IslandWindow.Instance.Vanish(false);
        }));
    }
    static void Toggle()
    {
        if (_t.IsEnabled) { _t.Stop(); _status = "Stopped."; }
        else if (_area == null) _status = "Choose an area first.";
        else { _sig = null; _changed = false; _since = DateTime.Now.ToString("t"); _status = "Watching since " + _since; _t.Start(); }
        Refresh();
    }
    static byte[] Sig(System.Drawing.Bitmap b)
    {
        using var small = new System.Drawing.Bitmap(b, 24, 24); var o = new byte[24 * 24];
        for (int y = 0; y < 24; y++) for (int x = 0; x < 24; x++) { var c = small.GetPixel(x, y); o[y * 24 + x] = (byte)((c.R + c.G + c.B) / 3); }
        return o;
    }
    static async System.Threading.Tasks.Task Poll()
    {
        if (_busy || _area == null) return; _busy = true;
        try
        {
            using var bmp = Shot.Grab(_area.Value, false); var sig = Sig(bmp);
            bool diff = _sig != null && sig.Zip(_sig, (a, b) => Math.Abs(a - b)).Sum() > 24 * 24 * 1.5;
            if (_sig != null && diff) { _changed = true; _changedAt = DateTime.Now; }
            _sig = sig;
            string? hit = null;
            if (_cond == "changes" && diff) hit = "It changed";
            else if (_cond == "settles" && _changed && (DateTime.Now - _changedAt).TotalSeconds >= 30) hit = "It stopped changing";
            else if (_cond is "contains" or "reaches")
            {
                using var ms = new MemoryStream(); using (var big = new System.Drawing.Bitmap(bmp, bmp.Width * 2, bmp.Height * 2)) big.Save(ms, System.Drawing.Imaging.ImageFormat.Png); ms.Position = 0;
                var dec = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(System.IO.WindowsRuntimeStreamExtensions.AsRandomAccessStream(ms));
                using var soft = await dec.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
                var eng = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
                if (eng != null)
                {
                    string text = (await eng.RecognizeAsync(soft)).Text;
                    if (_cond == "contains" && _text.Length > 0 && text.Contains(_text, StringComparison.OrdinalIgnoreCase)) hit = "It shows " + _text;
                    else if (_cond == "reaches" && double.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out var target))
                        foreach (Match m in Regex.Matches(text, @"\d+(?:[.,]\d+)?")) if (double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= target) { hit = "It reached " + m.Value; break; }
                }
            }
            if (hit != null) { _t.Stop(); _status = hit + " at " + DateTime.Now.ToString("t"); Svc.Notices.Add("Watch: " + hit, "", ""); try { System.Media.SystemSounds.Exclamation.Play(); } catch { } Refresh(); }
        }
        catch { }
        finally { _busy = false; }
    }
    public override void Tick() { if (_statusBlock != null) _statusBlock.Text = _status; }
}
