using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.IO.Path;
using System.Windows.Threading;
using static Perch.UI;

namespace Perch;

// Reads the usage that Claude Code and Codex record in their own session files on this PC. Read-only: nothing is written, nothing is sent,
// no sign-in is touched. Prompts and replies are never kept; only dates, model names, project folders and token counts.
public record AgentUse(DateTime At, string Agent, string Model, string Project, long In, long Out, long CacheRead, long CacheWrite);

public static class AgentUsage
{
    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string ClaudeDir => Path.Combine(Home, ".claude", "projects");
    public static string CodexDir => Path.Combine(Home, ".codex", "sessions");
    static readonly Dictionary<string, (long Size, DateTime Time, List<AgentUse> Uses)> _cache = new();
    public static List<AgentUse> All { get; private set; } = new(); public static DateTime LoadedAt; static bool _busy;
    public static bool Found(string agent) => Directory.Exists(agent == "Claude" ? ClaudeDir : CodexDir);

    // dollars per million tokens: input, output; cached reads cost a tenth of input, cache writes a quarter more
    public static (double In, double Out, double CacheRead, double CacheWrite)? Price(string model)
    {
        string m = model.ToLowerInvariant();
        if (m.Contains("opus")) return (15, 75, 1.5, 18.75);
        if (m.Contains("sonnet")) return (3, 15, 0.3, 3.75);
        if (m.Contains("haiku")) return (1, 5, 0.1, 1.25);
        if (m.Contains("gpt") || m.Contains("codex") || m.StartsWith("o")) return (1.25, 10, 0.125, 1.25);
        return null;
    }
    public static double Value(AgentUse u) { var p = Price(u.Model); return p == null ? 0 : (u.In * p.Value.In + u.Out * p.Value.Out + u.CacheRead * p.Value.CacheRead + u.CacheWrite * p.Value.CacheWrite) / 1e6; }
    static string ProjectName(string cwd) { cwd = cwd.TrimEnd('\\', '/'); int i = Math.Max(cwd.LastIndexOf('\\'), cwd.LastIndexOf('/')); return i >= 0 ? cwd[(i + 1)..] : cwd; }

    public static async Task Refresh()
    {
        if (_busy) return; _busy = true;
        try { await Task.Run(Scan); } catch { } finally { _busy = false; }
    }
    static void Scan()
    {
        var s = Settings.Current; var since = DateTime.Now.AddDays(-31); var seen = new HashSet<string>(); var files = new List<string>();
        try { if (s.AgentsClaude && Directory.Exists(ClaudeDir)) files.AddRange(Directory.EnumerateFiles(ClaudeDir, "*.jsonl", SearchOption.AllDirectories)); } catch { }
        try { if (s.AgentsCodex && Directory.Exists(CodexDir)) files.AddRange(Directory.EnumerateFiles(CodexDir, "*.jsonl", SearchOption.AllDirectories)); } catch { }
        var all = new List<AgentUse>();
        foreach (var f in files)
        {
            try
            {
                var fi = new FileInfo(f); if (fi.LastWriteTime < since) continue;
                if (_cache.TryGetValue(f, out var c) && c.Size == fi.Length && c.Time == fi.LastWriteTime) { all.AddRange(c.Uses); continue; }
                var uses = f.StartsWith(ClaudeDir, StringComparison.OrdinalIgnoreCase) ? ReadClaude(f) : ReadCodex(f);
                _cache[f] = (fi.Length, fi.LastWriteTime, uses); all.AddRange(uses);
            }
            catch { }
        }
        foreach (var k in _cache.Keys.Where(k => !files.Contains(k)).ToList()) _cache.Remove(k);
        All = all.Where(u => u.At >= since).ToList(); LoadedAt = DateTime.Now;
    }
    static List<AgentUse> ReadClaude(string file)
    {
        var res = new List<AgentUse>(); var ids = new HashSet<string>();
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var sr = new StreamReader(fs);
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            if (!line.Contains("\"usage\"", StringComparison.Ordinal) || !line.Contains("\"assistant\"", StringComparison.Ordinal)) continue;
            try
            {
                using var d = JsonDocument.Parse(line); var r = d.RootElement;
                if (!r.TryGetProperty("message", out var msg) || !msg.TryGetProperty("usage", out var u)) continue;
                string id = (msg.TryGetProperty("id", out var mid) ? mid.GetString() : "") + ":" + (r.TryGetProperty("requestId", out var rq) ? rq.GetString() : "");
                if (id.Length > 1 && !ids.Add(id)) continue;
                DateTime at = r.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String && DateTime.TryParse(ts.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var dt) ? dt.ToLocalTime() : File.GetLastWriteTime(file);
                string model = msg.TryGetProperty("model", out var mo) ? mo.GetString() ?? "" : ""; if (model == "<synthetic>") continue;
                string cwd = r.TryGetProperty("cwd", out var cw) ? cw.GetString() ?? "" : "";
                long G(string n) => u.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
                res.Add(new AgentUse(at, "Claude", model, ProjectName(cwd), G("input_tokens"), G("output_tokens"), G("cache_read_input_tokens"), G("cache_creation_input_tokens")));
            }
            catch { }
        }
        return res;
    }
    static List<AgentUse> ReadCodex(string file)
    {
        var res = new List<AgentUse>(); string model = "", cwd = "";
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var sr = new StreamReader(fs);
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            bool tc = line.Contains("\"token_count\"", StringComparison.Ordinal), meta = line.Contains("\"turn_context\"", StringComparison.Ordinal) || line.Contains("\"session_meta\"", StringComparison.Ordinal);
            if (!tc && !meta) continue;
            try
            {
                using var d = JsonDocument.Parse(line); var r = d.RootElement; if (!r.TryGetProperty("payload", out var p)) continue;
                if (meta) { if (p.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String) model = m.GetString() ?? model; if (p.TryGetProperty("cwd", out var c) && c.ValueKind == JsonValueKind.String) cwd = c.GetString() ?? cwd; continue; }
                if (!p.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object || !info.TryGetProperty("last_token_usage", out var u)) continue;
                DateTime at = r.TryGetProperty("timestamp", out var ts) && DateTime.TryParse(ts.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var dt) ? dt.ToLocalTime() : File.GetLastWriteTime(file);
                long G(string n) => u.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
                long cached = G("cached_input_tokens"); res.Add(new AgentUse(at, "Codex", model.Length > 0 ? model : "codex", ProjectName(cwd), Math.Max(0, G("input_tokens") - cached), G("output_tokens"), cached, 0));
            }
            catch { }
        }
        return res;
    }
    // Is an agent working right now: did one of its session files change in the last half minute.
    public static (bool Working, string Agent, string Project) Live()
    {
        try
        {
            var cut = DateTime.Now.AddSeconds(-25);
            foreach (var (dir, name) in new[] { (ClaudeDir, "Claude Code"), (CodexDir, "Codex") })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.AllDirectories))
                    if (File.GetLastWriteTime(f) > cut) { string proj = name == "Claude Code" ? Path.GetFileName(Path.GetDirectoryName(f)) ?? "" : ""; return (true, name, proj.Contains('-') ? proj[(proj.LastIndexOf('-') + 1)..] : proj); }
            }
        }
        catch { }
        return (false, "", "");
    }
}

public static class AgentWatch
{
    static readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromSeconds(12) }; static bool _was; static DateTime _since; static string _agent = "", _proj = "";
    public static void Start()
    {
        _t.Tick += (_, _) =>
        {
            if (!Settings.Current.AgentsFinishAlert) { _was = false; return; }
            _ = Task.Run(AgentUsage.Live).ContinueWith(t => Svc.Ui(() =>
            {
                var (w, a, p) = t.Result;
                if (w && !_was) { _since = DateTime.Now; _agent = a; _proj = p; }
                if (!w && _was && (DateTime.Now - _since).TotalMinutes >= Settings.Current.AgentsFinishMinutes) Svc.Notices.Add(_agent + " finished", (_proj.Length > 0 ? _proj + ", " : "") + $"{(DateTime.Now - _since).TotalMinutes:0} min.", "");
                _was = w;
            }));
        };
        _t.Start();
    }
}

public class AgentsTab : TabBase
{
    public override string Id => "agents"; public override string Name => "AI agents"; public override string Glyph => ""; public override double Height => 312;
    string _period = "Today"; readonly StackPanel _body = new(); readonly Border _host = new();
    protected override FrameworkElement Build() { _host.Child = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden }.With(s => s.PreviewMouseWheel += (o, e) => { s.ScrollToVerticalOffset(s.VerticalOffset - e.Delta * 0.6); e.Handled = true; }); _host.Margin = new Thickness(14, 4, 14, 12); Paint(); return _host; }
    public override void Shown() { _ = AgentUsage.Refresh().ContinueWith(_ => Svc.Ui(Paint)); Paint(); }
    public override void Tick() { }
    static string K(long n) => n >= 1_000_000_000 ? $"{n / 1e9:0.0}B" : n >= 1_000_000 ? $"{n / 1e6:0.0}M" : n >= 1000 ? $"{n / 1e3:0.#}K" : n.ToString();
    void Paint()
    {
        _body.Children.Clear();
        var chips = new WrapPanel();
        foreach (var p in new[] { "Today", "7 days", "30 days" }) { var pp = p; bool on = pp == _period; chips.Children.Add(Btn(T(pp, 11, on ? "#000000" : "#E5E5EA", true), () => { _period = pp; Paint(); }, on ? "#FFFFFF" : Card, 9, new Thickness(10, 5, 10, 5)).M(0, 0, 5, 5)); }
        chips.Children.Add(Pill("Refresh", () => { _ = AgentUsage.Refresh().ContinueWith(_ => Svc.Ui(Paint)); }).M(4, 0, 0, 5));
        _body.Children.Add(chips);
        var s = Settings.Current;
        if (!AgentUsage.Found("Claude") && !AgentUsage.Found("Codex")) { _body.Children.Add(T("No usage from Claude Code or Codex yet. It appears here as soon as either works on this PC. Perch reads their session files, which stay on this PC.", 11.5, Dim).With(t => t.TextWrapping = TextWrapping.Wrap)); return; }
        if (AgentUsage.LoadedAt == DateTime.MinValue) { _body.Children.Add(T("Reading usage…", 11.5, Dim)); _ = AgentUsage.Refresh().ContinueWith(_ => Svc.Ui(Paint)); return; }
        var from = _period == "Today" ? DateTime.Today : _period == "7 days" ? DateTime.Today.AddDays(-6) : DateTime.Today.AddDays(-29);
        var rows = AgentUsage.All.Where(u => u.At >= from).ToList();
        long tokens = rows.Sum(u => u.In + u.Out + u.CacheRead + u.CacheWrite), cached = rows.Sum(u => u.CacheRead); double value = rows.Sum(AgentUsage.Value); bool unpriced = rows.Any(u => AgentUsage.Price(u.Model) == null);
        var live = AgentUsage.Live();
        var top = MakeGrid("*,*,*", "auto");
        FrameworkElement Stat(string label, string val) => Col(T(val, 20, "#FFFFFF", true), T(label, 10.5, Dim));
        top.Children.Add(Stat("tokens", K(tokens)).At(0, 0)); top.Children.Add(Stat("API value" + (unpriced ? " (at least)" : ""), "$" + value.ToString(value >= 100 ? "0" : "0.00")).At(0, 1)); top.Children.Add(Stat("from cache", tokens == 0 ? "0%" : $"{cached * 100.0 / tokens:0}%").At(0, 2));
        _body.Children.Add(top.M(0, 2, 0, 8));
        if (s.AgentsDailyBudget > 0 && _period == "Today") _body.Children.Add(T($"Budget: ${Math.Max(0, s.AgentsDailyBudget - value):0.00} left of ${s.AgentsDailyBudget:0.00}", 11, value > s.AgentsDailyBudget ? "#FF453A" : Dim).M(0, 0, 0, 6));
        _body.Children.Add(T(live.Working ? $"Working now: {live.Agent}" + (live.Project.Length > 0 ? " · " + live.Project : "") : "No agent working right now", 11, live.Working ? "#30D158" : Dim, live.Working).M(0, 0, 0, 8));
        // the last 14 days as bars
        var days = Enumerable.Range(0, 14).Select(i => DateTime.Today.AddDays(i - 13)).ToList(); var byDay = days.Select(d => AgentUsage.All.Where(u => u.At.Date == d).Sum(u => u.In + u.Out + u.CacheRead + u.CacheWrite)).ToList(); long max = Math.Max(1, byDay.Max());
        var bars = new StackPanel { Orientation = Orientation.Horizontal, Height = 44, VerticalAlignment = VerticalAlignment.Bottom };
        for (int i = 0; i < days.Count; i++) { int ii = i; var col = new Border { Width = 18, Height = Math.Max(2, 40.0 * byDay[ii] / max), Background = B(days[ii] == DateTime.Today ? "#FFFFFF" : "#8E8E93"), CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Bottom, ToolTip = days[ii].ToString("ddd d MMM") + ": " + K(byDay[ii]) + " tokens" }; bars.Children.Add(col); }
        _body.Children.Add(bars.M(0, 0, 0, 10));
        _body.Children.Add(T("MODELS", 10, Dim, false, true).M(0, 0, 0, 4));
        foreach (var g in rows.GroupBy(u => u.Model.Length > 0 ? u.Model : "unknown").OrderByDescending(g => g.Sum(u => u.In + u.Out + u.CacheRead + u.CacheWrite)).Take(4))
            _body.Children.Add(Row(T(g.Key, 11.5, "#FFFFFF").With(t => { t.Width = 190; t.TextTrimming = TextTrimming.CharacterEllipsis; }), T(K(g.Sum(u => u.In + u.Out + u.CacheRead + u.CacheWrite)), 11.5, Dim, false, true).With(t => t.Width = 60), T("$" + g.Sum(AgentUsage.Value).ToString("0.00"), 11.5, Dim, false, true)));
        _body.Children.Add(T("PROJECTS", 10, Dim, false, true).M(0, 8, 0, 4));
        foreach (var g in rows.GroupBy(u => u.Project.Length > 0 ? u.Project : "unknown").OrderByDescending(g => g.Sum(u => u.In + u.Out + u.CacheRead + u.CacheWrite)).Take(4))
            _body.Children.Add(Row(T(g.Key, 11.5, "#FFFFFF").With(t => { t.Width = 190; t.TextTrimming = TextTrimming.CharacterEllipsis; }), T(K(g.Sum(u => u.In + u.Out + u.CacheRead + u.CacheWrite)), 11.5, Dim, false, true).With(t => t.Width = 60), T("$" + g.Sum(AgentUsage.Value).ToString("0.00"), 11.5, Dim, false, true)));
        _body.Children.Add(T("API value is what the same work would cost at list prices. Plans charge a fixed price instead. It is an estimate from the models' names.", 10, Dim).With(t => { t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 10, 0, 0); }));
    }
}
