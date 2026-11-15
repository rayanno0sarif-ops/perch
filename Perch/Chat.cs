using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static Perch.UI;

namespace Perch;

// ───────────────────────── Extensions ─────────────────────────
// An extension is a small description of a tab: text, buttons that open a page, open an app, copy text,
// show a message or run a PowerShell line, and live status lines. The assistant writes these; nothing
// is added until you have read it and pressed Add, and any script is shown to you in full first.
public class ExtBlock
{
    public string Type { get; set; } = "text";   // text | tile | status
    public string Text { get; set; } = "";       // text blocks
    public string Label { get; set; } = "";      // tiles and status lines
    public string Glyph { get; set; } = "";      // Segoe Fluent Icons code in hex, like E774
    public string Action { get; set; } = "url";  // tile: url | app | copy | notice | script
    public string Value { get; set; } = "";      // the url, path, text or script
    public int EverySeconds { get; set; } = 60;  // status lines: how often the script runs
}
public class Extension
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Glyph { get; set; } = "E74C";
    public string Summary { get; set; } = "";
    public List<ExtBlock> Blocks { get; set; } = new();
    public bool HasScripts => Blocks.Any(b => (b.Type == "tile" && b.Action == "script") || b.Type == "status");
}

public static class Extensions
{
    public static string Dir => System.IO.Path.Combine(Settings.Dir, "extensions");
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static List<Extension> Load()
    {
        var list = new List<Extension>();
        try
        {
            if (!Directory.Exists(Dir)) return list;
            foreach (var f in Directory.GetFiles(Dir, "*.json").OrderBy(f => f))
                try { var e = JsonSerializer.Deserialize<Extension>(File.ReadAllText(f), Json); if (e != null && e.Id.Length > 0 && e.Blocks.Count > 0) list.Add(e); } catch { }
        }
        catch { }
        return list;
    }
    static string Slug(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    // Reads an extension out of text: the fenced block the assistant writes, or bare JSON.
    public static Extension? Parse(string text, out string error)
    {
        error = "";
        var m = Regex.Match(text, "```(?:perch-extension|json)?\\s*(\\{.*?\\})\\s*```", RegexOptions.Singleline);
        string json = m.Success ? m.Groups[1].Value : text.Trim();
        if (!json.StartsWith("{")) return null;
        try
        {
            var e = JsonSerializer.Deserialize<Extension>(json, Json);
            if (e == null || e.Blocks.Count == 0) { error = "That extension has nothing in it."; return null; }
            e.Id = Slug(string.IsNullOrWhiteSpace(e.Id) ? e.Name : e.Id);
            if (e.Id.Length == 0) { error = "That extension has no name."; return null; }
            e.Blocks = e.Blocks.Take(24).ToList(); // a tab has room for about this many
            foreach (var b in e.Blocks) { b.Type = b.Type.ToLowerInvariant(); b.Action = b.Action.ToLowerInvariant(); }
            return e;
        }
        catch (Exception ex) { error = "I could not read that extension: " + ex.Message; return null; }
    }
    public static void Save(Extension e)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(System.IO.Path.Combine(Dir, e.Id + ".json"), JsonSerializer.Serialize(e, Json));
        IslandWindow.Instance.RebuildTabs();
    }
    public static void Remove(string id)
    {
        try { File.Delete(System.IO.Path.Combine(Dir, id + ".json")); } catch { }
        IslandWindow.Instance.RebuildTabs();
    }
    // Runs one PowerShell line without a window; returns what it printed (first line or two) or the error.
    public static async Task<string> Run(string script, int timeoutSeconds = 12)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var read = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(timeoutSeconds * 1000)) { try { p.Kill(true); } catch { } return "Timed out after " + timeoutSeconds + " s"; }
            string output = (await read).Trim();
            if (output.Length == 0) { string err = (await p.StandardError.ReadToEndAsync()).Trim(); return err.Length > 0 ? err : "Done"; }
            return output.Length > 400 ? output[..399] + "…" : output;
        }
        catch (Exception e) { return "Could not run: " + e.Message; }
    }
}

public class ExtensionTab : TabBase
{
    readonly Extension _e;
    public ExtensionTab(Extension e) { _e = e; }
    public override string Id => "ext:" + _e.Id;
    public override string Name => _e.Name;
    public override string Glyph => int.TryParse(_e.Glyph, System.Globalization.NumberStyles.HexNumber, null, out var c) ? char.ConvertFromUtf32(c) : "";
    public override double Height
    {
        get
        {
            int tiles = _e.Blocks.Count(b => b.Type == "tile"), others = _e.Blocks.Count - tiles;
            return Math.Min(300, 62 + ((tiles + 2) / 3) * 50 + others * 24);
        }
    }
    readonly List<(ExtBlock B, TextBlock Out)> _live = new(); readonly Dictionary<ExtBlock, DateTime> _ran = new();
    protected override FrameworkElement Build()
    {
        var col = new StackPanel(); var tiles = new WrapPanel();
        foreach (var b in _e.Blocks)
        {
            if (b.Type == "tile")
            {
                var blk = b;
                var g = int.TryParse(b.Glyph, System.Globalization.NumberStyles.HexNumber, null, out var c) ? char.ConvertFromUtf32(c) : "";
                var t = Btn(Col(Ico(g, 15), T(b.Label, 10.5, "#FFFFFF", true).M(0, 5).With(x => { x.HorizontalAlignment = HorizontalAlignment.Center; x.TextAlignment = TextAlignment.Center; })), () => Act(blk), Card, 12, new Thickness(4, 9, 4, 8));
                t.Width = 112; t.Margin = new Thickness(0, 0, 8, 8); t.ToolTip = b.Action == "script" ? "Runs a script you approved when you added this" : b.Value;
                tiles.Children.Add(t);
            }
            else if (b.Type == "status")
            {
                var o = T("…", 11.5, "#FFFFFF", false, true);
                _live.Add((b, o));
                col.Children.Add(Row(T(b.Label, 11.5, Dim).With(x => x.Width = 130), o).M(2, 0, 0, 6));
            }
            else col.Children.Add(T(b.Text, 11.5, "#E5E5EA").With(x => { x.TextWrapping = TextWrapping.Wrap; x.Margin = new Thickness(2, 0, 0, 8); }));
        }
        col.Children.Add(tiles);
        return new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden }.With(s => s.Margin = new Thickness(14, 4, 14, 12));
    }
    public override void Shown() { foreach (var (b, o) in _live) Poll(b, o, true); }
    public override void Tick() { foreach (var (b, o) in _live) Poll(b, o, false); }
    async void Poll(ExtBlock b, TextBlock o, bool force)
    {
        if (!force && _ran.TryGetValue(b, out var at) && DateTime.Now - at < TimeSpan.FromSeconds(Math.Max(5, b.EverySeconds))) return;
        _ran[b] = DateTime.Now; // set first, so a slow script is never started twice
        var text = await Extensions.Run(b.Value);
        o.Text = text.Replace('\r', ' ').Replace('\n', ' ');
    }
    async void Act(ExtBlock b)
    {
        try
        {
            switch (b.Action)
            {
                case "url": case "app": Svc.Open(b.Value); break;
                case "copy": Clipboard.SetText(b.Value); Svc.Notices.Add("Copied", b.Label, ""); break;
                case "notice": Svc.Notices.Add(b.Label, b.Value, ""); break;
                case "script": { var r = await Extensions.Run(b.Value); Svc.Notices.Add(b.Label, r.Replace('\r', ' ').Replace('\n', ' '), ""); break; }
            }
        }
        catch { Svc.Notices.Add("That did not work", b.Label, ""); }
    }
}

// ───────────────────────── Keeping the API key safe ─────────────────────────
// Encrypted for this Windows account with the same Windows service that protects saved passwords.
public static class Secret
{
    [StructLayout(LayoutKind.Sequential)] struct BLOB { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)] static extern bool CryptProtectData(ref BLOB i, string? d, IntPtr e, IntPtr r, IntPtr p, int f, out BLOB o);
    [DllImport("crypt32.dll", SetLastError = true)] static extern bool CryptUnprotectData(ref BLOB i, IntPtr d, IntPtr e, IntPtr r, IntPtr p, int f, out BLOB o);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr h);
    static byte[]? Apply(byte[] data, bool protect)
    {
        var inp = new BLOB { Size = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        try
        {
            Marshal.Copy(data, 0, inp.Data, data.Length);
            if (!(protect ? CryptProtectData(ref inp, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var o) : CryptUnprotectData(ref inp, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out o))) return null;
            var res = new byte[o.Size]; Marshal.Copy(o.Data, res, 0, o.Size); LocalFree(o.Data); return res;
        }
        finally { Marshal.FreeHGlobal(inp.Data); }
    }
    public static string Seal(string plain) => plain.Length == 0 ? "" : Convert.ToBase64String(Apply(Encoding.UTF8.GetBytes(plain), true) ?? Array.Empty<byte>());
    public static string Open(string sealedText)
    {
        if (sealedText.Length == 0) return "";
        try { var b = Apply(Convert.FromBase64String(sealedText), false); return b == null ? "" : Encoding.UTF8.GetString(b); } catch { return ""; }
    }
}

// ───────────────────────── The assistant ─────────────────────────
public class ChatTab : TabBase
{
    public override string Id => "ai"; public override string Name => "Assistant"; public override string Glyph => "";
    public override double Height => 300;
    readonly StackPanel _log = new(); ScrollViewer _scroll = null!; TextBox _box = null!; TextBlock _hint = null!;
    readonly List<(string Role, string Text)> _history = new(); bool _busy;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    const string System_ = @"You are the assistant built into Perch, a notch-style island at the top of a Windows screen. You can chat, and you can add features to the island by writing an extension.

An extension is a new tab. When the user asks for a feature, reply with a short plain explanation and then ONE fenced block, exactly like this:

```perch-extension
{
  ""id"": ""short-slug"",
  ""name"": ""Tab name"",
  ""glyph"": ""E774"",
  ""summary"": ""One sentence on what it does."",
  ""blocks"": [
    { ""type"": ""text"", ""text"": ""Some words."" },
    { ""type"": ""tile"", ""label"": ""Open docs"", ""glyph"": ""E774"", ""action"": ""url"", ""value"": ""https://example.com"" },
    { ""type"": ""tile"", ""label"": ""Copy hello"", ""action"": ""copy"", ""value"": ""hello"" },
    { ""type"": ""tile"", ""label"": ""Free disk"", ""action"": ""script"", ""value"": ""(Get-PSDrive C).Free / 1GB -as [int]"" },
    { ""type"": ""status"", ""label"": ""Uptime"", ""value"": ""((Get-Date) - (gcim Win32_OperatingSystem).LastBootUpTime).ToString('d\\.hh\\:mm')"", ""everySeconds"": 60 }
  ]
}
```

Rules:
- Block types: text (uses text), tile (a button: label, optional glyph, action = url | app | copy | notice | script, value), status (a live line: label, a PowerShell line in value that prints one short result, everySeconds).
- glyph is a Segoe Fluent Icons code in hex, like E774 (globe), E8C8 (copy), E756 (command), E713 (settings), E7C3 (page).
- Scripts are PowerShell one-liners. Keep them small, read-only where you can, and never delete, change settings, install software, send data anywhere or touch credentials unless the user plainly asked for exactly that. The user reads every script before it is added.
- At most 24 blocks. Use id as a lowercase slug. Only write an extension when the user wants a feature; for ordinary questions just answer, briefly.
- You cannot change Perch's own code. If a request needs that, say so and offer the closest extension.";

    protected override FrameworkElement Build()
    {
        _scroll = new ScrollViewer { Content = _log, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        _scroll.PreviewMouseWheel += (_, e) => { _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset - e.Delta * 0.7); e.Handled = true; };
        _box = new TextBox { Background = Brushes.Transparent, Foreground = Brushes.White, CaretBrush = Brushes.White, BorderThickness = new Thickness(0), FontFamily = Font, FontSize = 12.5, AcceptsReturn = false, VerticalAlignment = VerticalAlignment.Center };
        _hint = T("Ask anything, or ask for a feature…", 12.5, Dim); _hint.IsHitTestVisible = false;
        _box.TextChanged += (_, _) => _hint.Visibility = _box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Send(); } };
        var field = new Grid(); field.Children.Add(_hint); field.Children.Add(_box);
        var send = IconBtn("", Send, 13, "#FFFFFF", 28); send.ToolTip = "Send";
        var inputRow = MakeGrid("*,auto", "auto"); inputRow.Children.Add(field.At(0, 0)); inputRow.Children.Add(send.At(0, 1));
        var input = new Border { CornerRadius = new CornerRadius(12), BorderBrush = Glass.CardRim, BorderThickness = new Thickness(1), Background = B("#14FFFFFF"), Padding = new Thickness(12, 4, 6, 4), Child = inputRow };
        input.MouseLeftButtonDown += (_, e) => { Window.GetWindow(_box)?.Activate(); _box.Focus(); Keyboard.Focus(_box); e.Handled = true; };
        var g = MakeGrid("*", "*,auto"); g.Children.Add(_scroll.At(0, 0)); g.Children.Add(input.M(0, 8, 0, 0).At(1, 0));
        g.Margin = new Thickness(14, 4, 14, 12);
        Welcome();
        return g;
    }

    static string ApiKey => Secret.Open(Settings.Current.AiKey);
    void Welcome()
    {
        if (ApiKey.Length == 0)
            Say("assistant", "To chat, add your own Anthropic API key in Settings → Assistant. Your messages go to Anthropic with that key and nowhere else; the key stays encrypted on this PC.\n\nOnce it is set, ask me for a feature, like “a tab with buttons for my three most used sites and my free disk space”, and I will write it for you to approve.");
        else Say("assistant", "Hi. Ask me anything, or ask for a feature to add to the island.");
    }

    void Say(string role, string text)
    {
        bool me = role == "user";
        var tb = T(text, 12, "#FFFFFF"); tb.TextWrapping = TextWrapping.Wrap; tb.VerticalAlignment = VerticalAlignment.Top;
        var bubble = new Border
        {
            Background = B(me ? "#3A64E0FF" : "#22FFFFFF"), CornerRadius = new CornerRadius(13), Padding = new Thickness(11, 7, 11, 8), Child = tb,
            HorizontalAlignment = me ? HorizontalAlignment.Right : HorizontalAlignment.Left, MaxWidth = 300, Margin = new Thickness(me ? 40 : 0, 0, me ? 0 : 40, 8)
        };
        _log.Children.Add(bubble); _scroll.ScrollToBottom();
    }

    async void Send()
    {
        string text = _box.Text.Trim();
        if (text.Length == 0 || _busy) return;
        _box.Clear(); Say("user", text); _history.Add(("user", text));
        // an extension pasted straight in is read here, no network needed
        var pasted = Extensions.Parse(text, out var perr);
        if (pasted != null) { Propose(pasted); return; }
        if (ApiKey.Length == 0) { Say("assistant", "I need your API key first: Settings → Assistant."); return; }
        _busy = true; var wait = T("Thinking…", 11, Dim); wait.Margin = new Thickness(4, 0, 0, 8); _log.Children.Add(wait); _scroll.ScrollToBottom();
        string reply;
        try { reply = await Ask(); }
        catch (Exception e) { reply = "I could not reach the model: " + e.Message; }
        _log.Children.Remove(wait); _busy = false;
        _history.Add(("assistant", reply));
        var ext = Extensions.Parse(reply, out _);
        string spoken = Regex.Replace(reply, "```.*?```", "", RegexOptions.Singleline).Trim();
        if (spoken.Length > 0) Say("assistant", spoken);
        if (ext != null) Propose(ext);
    }

    async Task<string> Ask()
    {
        var msgs = _history.TakeLast(16).Select(h => new { role = h.Role, content = h.Text }).ToList();
        var body = JsonSerializer.Serialize(new { model = Settings.Current.AiModel, max_tokens = 2048, system = System_, messages = msgs });
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.Add("x-api-key", ApiKey); req.Headers.Add("anthropic-version", "2023-06-01");
        using var res = await Http.SendAsync(req);
        string json = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            string msg = json; try { using var d = JsonDocument.Parse(json); msg = d.RootElement.GetProperty("error").GetProperty("message").GetString() ?? json; } catch { }
            throw new Exception($"{(int)res.StatusCode} {msg}");
        }
        using var doc = JsonDocument.Parse(json);
        var sb = new StringBuilder();
        foreach (var part in doc.RootElement.GetProperty("content").EnumerateArray())
            if (part.TryGetProperty("text", out var t)) sb.Append(t.GetString());
        return sb.ToString();
    }

    // The proposal card: what it is, every script in full, and the button that adds it.
    void Propose(Extension ext)
    {
        var col = new StackPanel();
        col.Children.Add(T(ext.Name, 12.5, "#FFFFFF", true));
        if (ext.Summary.Length > 0) col.Children.Add(T(ext.Summary, 11, "#C7C7CC").With(x => { x.TextWrapping = TextWrapping.Wrap; x.Margin = new Thickness(0, 2, 0, 0); }));
        int tiles = ext.Blocks.Count(b => b.Type == "tile"), status = ext.Blocks.Count(b => b.Type == "status");
        col.Children.Add(T($"{tiles} button{(tiles == 1 ? "" : "s")}, {status} live line{(status == 1 ? "" : "s")}", 10.5, Dim).M(0, 4, 0, 0));
        var scripts = ext.Blocks.Where(b => (b.Type == "tile" && b.Action == "script") || b.Type == "status").ToList();
        if (scripts.Count > 0)
        {
            col.Children.Add(T("This runs PowerShell on your PC with your permissions. Read it first:", 10.5, "#FFD60A", true).With(x => { x.TextWrapping = TextWrapping.Wrap; x.Margin = new Thickness(0, 8, 0, 4); }));
            foreach (var s in scripts)
                col.Children.Add(new Border { Background = B("#33000000"), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 4),
                    Child = T(s.Label + ":  " + s.Value, 10.5, "#E5E5EA", false, true).With(x => x.TextWrapping = TextWrapping.Wrap) });
        }
        var actions = Row(Pill("Add to island", () =>
        {
            try { Extensions.Save(ext); Svc.Notices.Add("Added " + ext.Name, "It is a new tab on the island.", ""); Say("assistant", $"Added. Look for “{ext.Name}” in the tab bar."); IslandWindow.Instance.Select("ext:" + ext.Id); }
            catch (Exception e) { Say("assistant", "I could not save it: " + e.Message); }
        }, true), Pill("Not now", () => Say("assistant", "Okay, I left it out.")).M(6));
        col.Children.Add(actions.M(0, 6, 0, 0));
        _log.Children.Add(new Border { Background = B("#1FFFFFFF"), BorderBrush = Glass.CardRim, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 40, 8), Child = col });
        _scroll.ScrollToBottom();
    }
}
