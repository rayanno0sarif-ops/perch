using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using static Perch.UI;

namespace Perch;

public abstract class TabBase
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Glyph { get; }
    public virtual double Height => 180;
    FrameworkElement? _view;
    public FrameworkElement View => _view ??= Build();
    protected abstract FrameworkElement Build();
    public virtual void Tick() { }
    public virtual void Shown() { }
    protected static IslandWindow Island => IslandWindow.Instance;

    public static List<TabBase> All() => new List<TabBase>
    {
        new MusicTab(), new SitesTab(), new NoticesTab(), new ShelfTab(), new ClipTab(),
        new WidgetsTab(), new ControlsTab(), new MixerTab(), new ToolsTab(), new AppsTab(), new UtilitiesTab(), new NotesTab(), new RemindersTab(), new CallTab(), new SystemTab(), new ManageTab(), new ChatTab()
    }.Concat(NewTabs()).Concat(Extensions.Load().Select(e => (TabBase)new ExtensionTab(e))).ToList();

    // Tabs added later start hidden, so an update never grows the island on its own. Turn one on under Panels.
    static IEnumerable<TabBase> NewTabs()
    {
        var tabs = new List<TabBase> { new TimerTab(), new CalendarTab(), new LyricsTab(), new WatchTab(), new AgentsTab() };
        var s = Settings.Current; bool changed = false;
        foreach (var t in tabs) if (!s.SeenTabs.Contains(t.Id)) { s.SeenTabs.Add(t.Id); if (!s.HiddenTabs.Contains(t.Id)) s.HiddenTabs.Add(t.Id); changed = true; }
        if (changed) s.Save();
        return tabs;
    }

    protected static FrameworkElement Empty(string glyph, string title, string body)
    {
        var art = new Border
        {
            Width = 64, Height = 52, CornerRadius = new CornerRadius(10), BorderBrush = B("#3A3A3C"), BorderThickness = new Thickness(1.2),
            Child = Ico(glyph, 18, "#D1D1D6")
        };
        var body2 = T(body, 11, Dim); body2.TextWrapping = TextWrapping.Wrap; body2.MaxWidth = 230;
        var row = Row(art, Col(T(title, 12.5, "#FFFFFF", true), body2.M(0, 2)).M(14));
        row.HorizontalAlignment = HorizontalAlignment.Center; row.VerticalAlignment = VerticalAlignment.Center;
        return row;
    }
}

// ───────────────────────── Music ─────────────────────────
public class MusicTab : TabBase
{
    public override string Id => "music"; public override string Name => "Music"; public override string Glyph => "";
    public override double Height => 166;
    Image _art; TextBlock _title, _artist, _pos, _left, _play, _volIco; MiniSlider _seek, _vol; Border _artBox; TextBlock _artIco;

    protected override FrameworkElement Build()
    {
        _art = new Image { Stretch = Stretch.UniformToFill };
        _artIco = Ico("", 22, "#8E8E93");
        var artGrid = new Grid(); artGrid.Children.Add(_artIco); artGrid.Children.Add(_art);
        _artBox = new Border { Width = 58, Height = 58, CornerRadius = new CornerRadius(11), Background = B("#26FFFFFF"), Child = artGrid };
        _artBox.SizeChanged += (_, _) => _artBox.Clip = new RectangleGeometry(new Rect(0, 0, 58, 58), 11, 11);
        _title = T("No music right now", 14.5, "#FFFFFF", true);
        _artist = T("Start something in any app", 11.5, "#C7C7CC");
        _play = Ico("", 20);
        var controls = Row(IconBtn("", Svc.Media.Prev, 13, "#D1D1D6", 30), Btn(_play, Svc.Media.Toggle, "#00FFFFFF", 18, new Thickness(0)).With(b => { b.Width = b.Height = 36; }).M(6, 0, 6), IconBtn("", Svc.Media.Next, 13, "#D1D1D6", 30));
        controls.VerticalAlignment = VerticalAlignment.Center;

        var top = MakeGrid("auto,*,auto", "auto");
        top.Children.Add(_artBox.At(0, 0));
        top.Children.Add(Col(_title, _artist.M(0, 2)).M(14, 0, 8).At(0, 1).With(c => c.VerticalAlignment = VerticalAlignment.Center));
        top.Children.Add(controls.At(0, 2));

        _seek = new MiniSlider(3) { Margin = new Thickness(0, 10, 0, 0) };
        _seek.Committed += Svc.Media.Seek;
        _pos = T("0:00", 10.5, "#D1D1D6", false, true); _left = T("-0:00", 10.5, "#D1D1D6", false, true);
        _left.HorizontalAlignment = HorizontalAlignment.Right;
        _volIco = Ico("", 13, "#D1D1D6");
        _vol = new MiniSlider(3) { Width = 84, Margin = new Thickness(8, 0, 0, 0) };
        _vol.Changed += v => Svc.Speaker.Volume = v;
        var volRow = Row(Btn(_volIco, () => { Svc.Speaker.Muted = !Svc.Speaker.Muted; Tick(); }, "#00FFFFFF", 10, new Thickness(4)), _vol);
        volRow.HorizontalAlignment = HorizontalAlignment.Center;
        var bottom = MakeGrid("60,*,60", "auto");
        bottom.Children.Add(_pos.At(0, 0)); bottom.Children.Add(volRow.At(0, 1)); bottom.Children.Add(_left.At(0, 2));

        Svc.Media.Changed += Update;
        Update();
        return Col(top, _seek, bottom.M(0, 2)).M(16, 8, 16, 0);
    }
    void Update()
    {
        var m = Svc.Media;
        _title.Text = m.HasSession ? (string.IsNullOrEmpty(m.Title) ? "Unknown title" : m.Title) : "No music right now";
        _artist.Text = m.HasSession ? m.Artist : "Start something in any app";
        _art.Source = m.Art; _artIco.Visibility = m.Art == null ? Visibility.Visible : Visibility.Collapsed;
        _play.Text = m.Playing ? "" : "";
        Tick();
        Island.RefreshChrome();
    }
    public override void Tick()
    {
        if (_seek == null) return;
        var m = Svc.Media;
        var d = m.Duration; var p = m.Position;
        if (!_seek.Dragging) _seek.Value = d.TotalSeconds > 0 ? p.TotalSeconds / d.TotalSeconds : 0;
        _pos.Text = Fmt(p); _left.Text = d > TimeSpan.Zero ? "-" + Fmt(d - p) : "";
        if (!_vol.Dragging) _vol.Value = Svc.Speaker.Volume;
        _volIco.Text = Svc.Speaker.Muted ? "" : "";
    }
}

// ───────────────────────── Sites ─────────────────────────
public class SitesTab : TabBase
{
    public override string Id => "sites"; public override string Name => "Sites"; public override string Glyph => "";
    public override double Height => 214;
    WrapPanel _wrap; bool _adding;

    protected override FrameworkElement Build()
    {
        _wrap = new WrapPanel();
        var hint = T("Keep the pages you open every day one click away.", 11, Dim);
        hint.VerticalAlignment = VerticalAlignment.Bottom;
        var g = MakeGrid("*", "*,auto");
        g.Children.Add(new ScrollViewer { Content = _wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden }.At(0, 0));
        g.Children.Add(hint.M(2, 6, 0, 12).At(1, 0));
        g.Margin = new Thickness(14, 6, 14, 0);
        Fill();
        return g;
    }
    void Fill()
    {
        _wrap.Children.Clear();
        foreach (var s in Settings.Current.Sites)
        {
            var site = s;
            var host = Uri.TryCreate(s.Url, UriKind.Absolute, out var u) ? u.Host.Replace("www.", "") : s.Url;
            var badge = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = B(s.Color), Child = T(s.Name.Length > 0 ? s.Name[..1].ToUpper() : "?", 13, "#FFFFFF", true).With(t => t.HorizontalAlignment = HorizontalAlignment.Center) };
            var tile = Btn(Row(badge, Col(T(s.Name, 11.5, "#FFFFFF", true), T(host, 10.5, Dim)).M(8).With(c => { c.Width = 62; c.VerticalAlignment = VerticalAlignment.Center; })), () => Svc.Open(site.Url), Card, 12, new Thickness(8));
            tile.Width = 116; tile.Margin = new Thickness(0, 0, 8, 8);
            tile.ToolTip = "Right-click to remove";
            tile.MouseRightButtonUp += (_, _) => { Settings.Current.Sites.Remove(site); Settings.Current.Save(); Fill(); };
            _wrap.Children.Add(tile);
        }
        if (_adding)
        {
            var box = new TextBox
            {
                Width = 210, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), CaretBrush = Brushes.White,
                FontFamily = Font, FontSize = 12, VerticalAlignment = VerticalAlignment.Center
            };
            void Commit()
            {
                var raw = box.Text.Trim();
                _adding = false;
                if (raw.Length > 0)
                {
                    if (!raw.Contains("://")) raw = "https://" + raw;
                    if (Uri.TryCreate(raw, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http"))
                    {
                        var name = u.Host.Replace("www.", "").Split('.')[0];
                        name = char.ToUpper(name[0]) + name[1..];
                        string[] palette = { "#B3261E", "#1E6FB3", "#1E8E5A", "#8E4EC6", "#B3741E", "#3A3A3C" };
                        Settings.Current.Sites.Add(new Site { Name = name, Url = u.ToString(), Color = palette[Math.Abs(name.GetHashCode()) % palette.Length] });
                        Settings.Current.Save();
                    }
                }
                Fill();
            }
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); else if (e.Key == Key.Escape) { _adding = false; Fill(); } };
            var wrap = Box(Row(Ico("", 12, Dim).M(0, 0, 8), box), Card, 12, new Thickness(10, 9, 10, 9));
            wrap.Margin = new Thickness(0, 0, 8, 8);
            _wrap.Children.Add(wrap);
            box.Loaded += (_, _) => { Island.Activate(); box.Focus(); };
        }
        else
        {
            var add = Btn(Row(Ico("", 11, "#D1D1D6"), T("Add a site", 11.5, "#D1D1D6", true).M(8)).With(r => r.HorizontalAlignment = HorizontalAlignment.Center),
                () => { _adding = true; Fill(); }, "#00FFFFFF", 12, new Thickness(8, 13, 8, 13));
            add.Width = 116; add.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)); add.BorderThickness = new Thickness(1);
            add.Margin = new Thickness(0, 0, 8, 8);
            _wrap.Children.Add(add);
        }
    }
}

// ───────────────────────── Notices ─────────────────────────
public class NoticesTab : TabBase
{
    public override string Id => "notices"; public override string Name => "Notices"; public override string Glyph => "";
    public override double Height => 230;
    StackPanel _list; TextBlock _count; Grid _root; FrameworkElement _empty; FrameworkElement _footer; ScrollViewer _scroll;

    protected override FrameworkElement Build()
    {
        _list = new StackPanel();
        _count = T("", 11, Dim);
        _footer = Row(_count, Btn(T("Clear all", 11, "#FFFFFF", true), Svc.Notices.Clear, "#00FFFFFF", 8, new Thickness(8, 3, 8, 3)).M(6));
        _footer.HorizontalAlignment = HorizontalAlignment.Center;
        _empty = Empty("", "All quiet", "Finished timers, captures and lines from your scripts land here.");
        _scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        _root = MakeGrid("*", "*,auto");
        _root.Children.Add(_scroll.At(0, 0)); _root.Children.Add(_empty.At(0, 0)); _root.Children.Add(_footer.M(0, 4, 0, 8).At(1, 0));
        _root.Margin = new Thickness(12, 6, 12, 0);
        Svc.Notices.Changed += Fill;
        Fill();
        return _root;
    }
    static string Ago(DateTime at)
    {
        var d = DateTime.Now - at;
        return d.TotalMinutes < 1 ? "now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes}m" : d.TotalDays < 1 ? $"{(int)d.TotalHours}h" : $"{(int)d.TotalDays}d";
    }
    void Fill()
    {
        _list.Children.Clear();
        var items = Svc.Notices.Items;
        foreach (var n in items)
        {
            var notice = n;
            var badge = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Background = B("#33FFFFFF"), Child = Ico(n.Glyph, 12) };
            var body = T(n.Body, 10.5, "#C7C7CC");
            var g = MakeGrid("auto,*,auto", "auto");
            g.Children.Add(badge.At(0, 0));
            g.Children.Add(Col(T(n.Title, 11.5, "#FFFFFF", true), body).M(10, 0, 8).At(0, 1));
            g.Children.Add(T(Ago(n.At), 10, Dim).With(t => t.VerticalAlignment = VerticalAlignment.Top).At(0, 2));
            var card = Btn(g, () => Svc.Notices.Remove(notice), Card, 12, new Thickness(9));
            card.Margin = new Thickness(0, 0, 0, 6); card.ToolTip = "Click to dismiss";
            _list.Children.Add(card);
        }
        _count.Text = items.Count == 1 ? "1 notice" : $"{items.Count} notices";
        _empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _footer.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    public override void Shown() => Fill();
}

// ───────────────────────── Shelf ─────────────────────────
public class ShelfTab : TabBase
{
    public override string Id => "shelf"; public override string Name => "Shelf"; public override string Glyph => "";
    public override double Height => 158;
    StackPanel _files; Border _drop; ScrollViewer _scroll; Border _clear;

    protected override FrameworkElement Build()
    {
        _files = new StackPanel { Orientation = Orientation.Horizontal };
        _scroll = new ScrollViewer { Content = _files, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _scroll.PreviewMouseWheel += (_, e) => { _scroll.ScrollToHorizontalOffset(_scroll.HorizontalOffset - e.Delta / 2.0); e.Handled = true; };
        _drop = new Border
        {
            CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(0),
            Child = Empty("", "Park files on the shelf", "Drag anything onto the island and pick it up later.")
        };
        var dash = new Rectangle { RadiusX = 14, RadiusY = 14, Stroke = B("#55FFFFFF"), StrokeThickness = 1.2, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false };
        var dropGrid = new Grid(); dropGrid.Children.Add(dash); dropGrid.Children.Add(_drop);
        _clear = Pill("Clear", () => { Svc.Shelf.Clear(); Svc.RaiseShelf(); });
        var head = MakeGrid("*,auto", "auto");
        head.Children.Add(T("Shelf", 12.5, "#FFFFFF", true).At(0, 0)); head.Children.Add(_clear.At(0, 1));
        var g = MakeGrid("*", "auto,*");
        g.Children.Add(head.At(0, 0)); g.Children.Add(dropGrid.M(0, 8, 0, 12).At(1, 0)); g.Children.Add(_scroll.M(0, 8, 0, 12).At(1, 0));
        g.Margin = new Thickness(14, 4, 14, 0);
        g.Tag = dropGrid;
        Svc.ShelfChanged += Fill;
        _dropGrid = dropGrid;
        Fill();
        return g;
    }
    Grid _dropGrid;
    void Fill()
    {
        _files.Children.Clear();
        Svc.Shelf.RemoveAll(f => !File.Exists(f) && !Directory.Exists(f));
        foreach (var f in Svc.Shelf)
        {
            var file = f;
            var icon = new Image { Source = Native.FileIcon(f), Width = 30, Height = 30 };
            var name = T(System.IO.Path.GetFileName(f), 10.5, "#E5E5EA"); name.MaxWidth = 78; name.HorizontalAlignment = HorizontalAlignment.Center;
            var chip = Box(Col(icon, name.M(0, 6)), Card, 12, new Thickness(8, 10, 8, 8));
            chip.Width = 94; chip.Margin = new Thickness(0, 0, 8, 0); chip.Cursor = Cursors.Hand;
            chip.ToolTip = f + "\nDrag it out, double-click to open, right-click to remove";
            Point start = default; bool down = false;
            chip.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) { Svc.Open(file); return; }
                start = e.GetPosition(null); down = true;
            };
            chip.MouseLeftButtonUp += (_, _) => down = false;
            chip.MouseMove += (_, e) =>
            {
                if (!down || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(null);
                if (Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y) < 8) return; // hysteresis before a drag starts
                down = false;
                Island.Hold(true);
                try { DragDrop.DoDragDrop(chip, new DataObject(DataFormats.FileDrop, new[] { file }), DragDropEffects.Copy | DragDropEffects.Move); }
                finally { Island.Hold(false); }
            };
            chip.MouseRightButtonUp += (_, _) => { Svc.Shelf.Remove(file); Svc.RaiseShelf(); };
            _files.Children.Add(chip);
        }
        bool any = Svc.Shelf.Count > 0;
        _dropGrid.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        _scroll.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        _clear.Visibility = any ? Visibility.Visible : Visibility.Hidden;
    }
}

// ───────────────────────── Clipboard ─────────────────────────
public class ClipTab : TabBase
{
    public override string Id => "clip"; public override string Name => "Clipboard"; public override string Glyph => "\uE77F";
    public override double Height => 214;
    string _filter = "All"; int _n;
    StackPanel _cards = new() { Orientation = Orientation.Horizontal }; ScrollViewer _scroll = null!; FrameworkElement _empty = null!; StackPanel _chips = new() { Orientation = Orientation.Horizontal };
    static readonly string[] Filters = { "All", "Text", "Links", "Pictures", "Snippets" };

    protected override FrameworkElement Build()
    {
        _scroll = new ScrollViewer { Content = _cards, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        // the wheel scrolls the row sideways, and a flick of the wheel moves it by roughly a card
        _scroll.PreviewMouseWheel += (_, e) => { _scroll.ScrollToHorizontalOffset(_scroll.HorizontalOffset - e.Delta * 0.9); e.Handled = true; };
        _empty = Empty("\uE77F", "Nothing here yet", "Text, links and pictures you copy appear here. Star one to keep it.");
        var clear = Btn(T("Clear", 11, "#C7C7CC", true), () => { Svc.Clip.ClearUnpinned(); }, "#00FFFFFF", 9, new Thickness(9, 5, 9, 5));
        clear.ToolTip = "Remove everything except starred clips";
        var top = MakeGrid("*,auto", "auto"); top.Children.Add(_chips.At(0, 0)); top.Children.Add(clear.At(0, 1));
        var g = MakeGrid("*", "auto,*");
        g.Children.Add(top.At(0, 0)); g.Children.Add(_scroll.M(0, 8, 0, 12).At(1, 0)); g.Children.Add(_empty.At(1, 0));
        g.Margin = new Thickness(14, 4, 14, 0);
        Svc.Clip.Changed += Fill;
        Chips(); Fill();
        return g;
    }
    public override void Shown() { Fill(); }
    public override void Tick() { if (++_n % 20 == 0) Fill(); } // keeps the ages current

    void Chips()
    {
        _chips.Children.Clear();
        foreach (var f in Filters)
        {
            var name = f; bool on = name == _filter;
            var chip = Btn(T(name, 11, on ? "#000000" : "#E5E5EA", true), () => { _filter = name; Chips(); _scroll.ScrollToHorizontalOffset(0); Fill(); }, on ? "#FFFFFF" : Card, 9, new Thickness(9, 5, 9, 5));
            chip.Margin = new Thickness(0, 0, 5, 0);
            _chips.Children.Add(chip);
        }
    }

    static bool IsLink(string t) => !t.Contains(' ') && !t.Contains('\n') && Uri.TryCreate(t.Trim(), UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https");
    static string Age(DateTime at)
    {
        var d = DateTime.Now - at;
        return d.TotalSeconds < 45 ? "now" : d.TotalMinutes < 60 ? $"{(int)Math.Max(1, d.TotalMinutes)}m" : d.TotalHours < 24 ? $"{(int)d.TotalHours}h" : $"{(int)d.TotalDays}d";
    }

    void Fill()
    {
        double keep = _scroll.HorizontalOffset;
        _cards.Children.Clear();
        var c = Svc.Clip; int shown = 0;
        if (_filter == "Snippets")
        {
            foreach (var s in Settings.Current.Snippets.ToList()) { _cards.Children.Add(MakeCard("Snippet", "\uE735", "saved", Preview(s, 5), $"{s.Length} chars", () => { try { Clipboard.SetDataObject(s, true); } catch { } }, null, () => { Settings.Current.Snippets.Remove(s); Settings.Current.Save(); Fill(); })); shown++; }
        }
        else
        {
            var pinned = Settings.Current.Pinned.Select(t => new ClipEntry { Text = t, Pinned = true }).ToList();
            var items = pinned.Concat(c.Entries.Where(e => e.IsImage || !c.IsPinned(e.Text!))).ToList();
            foreach (var entry in items)
            {
                bool link = entry.Text != null && IsLink(entry.Text);
                if (_filter == "Text" && (entry.IsImage || link)) continue;
                if (_filter == "Links" && !link) continue;
                if (_filter == "Pictures" && !entry.IsImage) continue;
                var e2 = entry; UIElement card;
                string age = e2.Pinned ? "kept" : Age(e2.At);
                Action copy = () => c.CopyBack(e2);
                if (e2.IsImage)
                {
                    var thumb = new Border { Height = 62, CornerRadius = new CornerRadius(7), Background = new ImageBrush(e2.Image) { Stretch = Stretch.UniformToFill } };
                    card = MakeCard("Picture", "\uEB9F", age, thumb, $"{e2.Image!.PixelWidth} \u00D7 {e2.Image.PixelHeight}", copy, null, () => c.Remove(e2));
                }
                else if (link)
                {
                    var u = new Uri(e2.Text!.Trim());
                    string rest = (u.AbsolutePath == "/" ? "" : u.AbsolutePath) + u.Query;
                    var body = Col(T(u.Host.StartsWith("www.") ? u.Host[4..] : u.Host, 12, "#FFFFFF", true).With(x => x.TextWrapping = TextWrapping.NoWrap), Preview(rest.Length == 0 ? " " : rest, 3, Dim).M(0, 3));
                    card = MakeCard("Link", "\uE71B", age, body, u.Host, copy, e2.Text, () => { c.Remove(e2); if (e2.Pinned) c.TogglePin(e2.Text!); });
                }
                else
                {
                    card = MakeCard("Text", "\uE8D2", age, Preview(e2.Text!, 5), $"{e2.Text!.Length} chars", copy, e2.Text, () => { c.Remove(e2); if (e2.Pinned) c.TogglePin(e2.Text!); });
                }
                _cards.Children.Add(card); shown++;
            }
        }
        _empty.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        _scroll.Visibility = shown == 0 ? Visibility.Collapsed : Visibility.Visible;
        _scroll.ScrollToHorizontalOffset(keep);
    }

    static TextBlock Preview(string text, int lines, string color = "#FFFFFF")
    {
        var tb = T(text.Replace("\r", "").Trim(), 11, color); tb.TextWrapping = TextWrapping.Wrap; tb.TextTrimming = TextTrimming.CharacterEllipsis;
        tb.MaxHeight = lines * 15; tb.VerticalAlignment = VerticalAlignment.Top;
        return tb;
    }

    // One clip as a card: kind and age on top, the clip itself, then a footer with a star (text and links) for keeping it.
    // Click copies it again; right-click removes it.
    Border MakeCard(string kind, string glyph, string age, UIElement body, string foot, Action copy, string? starText, Action remove)
    {
        var head = MakeGrid("*,auto", "auto");
        head.Children.Add(Row(Ico(glyph, 10.5, "#C7C7CC"), T(kind, 10.5, "#C7C7CC", true).M(5)).At(0, 0));
        head.Children.Add(T(age, 10.5, Dim).At(0, 1));
        var footer = MakeGrid("*,auto", "auto");
        var f = T(foot, 10, Dim); f.TextTrimming = TextTrimming.CharacterEllipsis;
        footer.Children.Add(f.At(0, 0));
        if (starText != null)
        {
            bool starred = Svc.Clip.IsPinned(starText);
            var star = IconBtn(starred ? "\uE735" : "\uE734", () => { Svc.Clip.TogglePin(starText); }, 11, starred ? "#FFD60A" : "#8E8E93", 22);
            star.ToolTip = starred ? "Starred: kept until you remove it" : "Star to keep";
            footer.Children.Add(star.M(0, -4, -4, -4).At(0, 1));
        }
        var g = MakeGrid("*", "auto,*,auto");
        if (body is FrameworkElement fe) fe.Margin = new Thickness(0, 8, 0, 6);
        Grid.SetRow(body, 1);
        g.Children.Add(head.At(0, 0)); g.Children.Add(body);
        g.Children.Add(footer.At(2, 0));
        var card = Btn(g, copy, Card, 13, new Thickness(11, 9, 10, 9));
        card.Width = 138; card.Height = 128; card.Margin = new Thickness(0, 0, 8, 0);
        card.ToolTip = "Click to copy again. Right-click to remove.";
        card.MouseRightButtonUp += (_, e) => { remove(); e.Handled = true; };
        return card;
    }
}

// ───────────────────────── Widgets ─────────────────────────
public class WidgetsTab : TabBase
{
    public override string Id => "widgets"; public override string Name => "Widgets"; public override string Glyph => "";
    public override double Height => 222;
    TextBlock _day, _dow, _mon, _week, _timer, _timerSub, _playIco, _temp, _cond, _hl, _wIco, _cpu, _ram, _net, _gpu;
    MiniSlider _cpuBar, _ramBar, _gpuBar; Grid _weekRow; Ellipse _ring; DateTime _built;

    protected override FrameworkElement Build()
    {
        // calendar
        _day = T("", 22, "#FFFFFF", true); _dow = T("", 10.5, "#FFFFFF", true); _mon = T("", 10.5, "#C7C7CC"); _week = T("", 10.5, Dim);
        _week.HorizontalAlignment = HorizontalAlignment.Right; _week.VerticalAlignment = VerticalAlignment.Top;
        _weekRow = new Grid();
        var calTop = MakeGrid("auto,*,auto", "auto");
        calTop.Children.Add(_day.At(0, 0)); calTop.Children.Add(Col(_dow, _mon).M(7).At(0, 1)); calTop.Children.Add(_week.At(0, 2));
        var cal = Box(Col(calTop, _weekRow.M(0, 5)), Card, 12, new Thickness(10, 7, 10, 7));

        // focus timer
        _timer = T("25:00", 11, "#FFFFFF", true, true); _timer.HorizontalAlignment = HorizontalAlignment.Center;
        _ring = new Ellipse { Width = 46, Height = 46, Stroke = B("#55FFFFFF"), StrokeThickness = 2 };
        var ringGrid = new Grid { Width = 46, Height = 46 }; ringGrid.Children.Add(_ring); ringGrid.Children.Add(_timer);
        _timerSub = T("", 10.5, Dim);
        _playIco = Ico("", 10, "#000000");
        var play = Btn(_playIco, Svc.Focus.Toggle, "#FFFFFF", 11, new Thickness(0)); play.Width = play.Height = 22;
        var minus = Btn(T("−", 12, "#FFFFFF", true).With(t => t.HorizontalAlignment = HorizontalAlignment.Center), () => Svc.Focus.Adjust(-5), Card, 11, new Thickness(0)); minus.Width = minus.Height = 22;
        var plus = Btn(T("+", 12, "#FFFFFF", true).With(t => t.HorizontalAlignment = HorizontalAlignment.Center), () => Svc.Focus.Adjust(5), Card, 11, new Thickness(0)); plus.Width = plus.Height = 22;
        var focus = Box(Row(ringGrid, Col(T("Focus timer", 11.5, "#FFFFFF", true), _timerSub, Row(play, minus.M(5), plus.M(5)).M(0, 5)).M(10)), Card, 12, new Thickness(10, 7, 10, 7));

        // weather
        _temp = T("—", 22, "#FFFFFF", true); _wIco = Ico("", 18, "#E5E5EA"); _wIco.HorizontalAlignment = HorizontalAlignment.Right;
        _cond = T("", 10.5, "#E5E5EA"); _hl = T("", 10.5, "#C7C7CC"); _hl.HorizontalAlignment = HorizontalAlignment.Right;
        var wg = MakeGrid("*,auto", "*,auto");
        wg.Children.Add(_temp.At(0, 0)); wg.Children.Add(_wIco.At(0, 1)); wg.Children.Add(_cond.At(1, 0)); wg.Children.Add(_hl.At(1, 1));
        var weather = Btn(wg, () => SettingsWindow.Open("Widgets"), Card, 12, new Thickness(10, 7, 10, 7));
        Svc.Weather.Changed += UpdateWeather;

        // stats
        _cpu = T("0%", 10.5, "#FFFFFF", true, true); _ram = T("0%", 10.5, "#FFFFFF", true, true); _net = T("", 10, "#C7C7CC", false, true);
        _cpuBar = new MiniSlider(2) { IsHitTestVisible = false }; _ramBar = new MiniSlider(2, false, "#FFD60A") { IsHitTestVisible = false };
        _gpu = T("n/a", 10.5, "#FFFFFF", true, true); _gpuBar = new MiniSlider(2, false, "#64D2FF") { IsHitTestVisible = false };
        var sg = MakeGrid("30,*,36", "auto,auto,auto,auto");
        sg.Children.Add(T("GPU", 10.5, "#E5E5EA").At(1, 0)); sg.Children.Add(_gpuBar.At(1, 1)); sg.Children.Add(_gpu.With(t => t.HorizontalAlignment = HorizontalAlignment.Right).At(1, 2));
        sg.Children.Add(T("CPU", 10.5, "#E5E5EA").At(0, 0)); sg.Children.Add(_cpuBar.At(0, 1)); sg.Children.Add(_cpu.With(t => t.HorizontalAlignment = HorizontalAlignment.Right).At(0, 2));
        sg.Children.Add(T("RAM", 10.5, "#E5E5EA").At(2, 0)); sg.Children.Add(_ramBar.At(2, 1)); sg.Children.Add(_ram.With(t => t.HorizontalAlignment = HorizontalAlignment.Right).At(2, 2));
        sg.Children.Add(_net.M(0, 3).At(3, 0, 1, 3));
        var stats = Box(sg, Card, 12, new Thickness(10, 6, 10, 6));

        var g = MakeGrid("*,6,*", "*,6,*");
        g.Children.Add(cal.At(0, 0)); g.Children.Add(focus.At(0, 2)); g.Children.Add(weather.At(2, 0)); g.Children.Add(stats.At(2, 2));
        g.Margin = new Thickness(12, 4, 12, 12);
        Svc.Focus.Changed += UpdateTimer;
        UpdateTimer(); UpdateWeather(); Calendar();
        return g;
    }
    void Calendar()
    {
        var now = DateTime.Now; _built = now.Date;
        _day.Text = now.Day.ToString(); _dow.Text = now.ToString("ddd").ToUpper(); _mon.Text = now.ToString("MMM").ToUpper();
        _week.Text = "Week " + ISOWeek.GetWeekOfYear(now);
        _weekRow.Children.Clear(); _weekRow.ColumnDefinitions.Clear();
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var start = now.Date.AddDays(-((7 + (int)now.DayOfWeek - (int)first) % 7));
        for (int i = 0; i < 7; i++)
        {
            var d = start.AddDays(i); bool today = d == now.Date;
            _weekRow.ColumnDefinitions.Add(new ColumnDefinition());
            var cell = Col(T(d.ToString("ddd")[..1], 9.5, today ? "#FFFFFF" : Dim, today).With(t => t.HorizontalAlignment = HorizontalAlignment.Center),
                           T(d.Day.ToString(), 10.5, "#FFFFFF", today).With(t => t.HorizontalAlignment = HorizontalAlignment.Center));
            var b = new Border { Child = cell, CornerRadius = new CornerRadius(6), Padding = new Thickness(0, 2, 0, 2), Background = today ? B("#40FFFFFF") : Brushes.Transparent };
            _weekRow.Children.Add(b.At(0, i));
        }
    }
    void UpdateTimer()
    {
        if (_timer == null) return;
        var f = Svc.Focus;
        _timer.Text = $"{(int)f.Remaining.TotalMinutes:00}:{f.Remaining.Seconds:00}";
        _timerSub.Text = f.Running ? "In focus" : $"{Settings.Current.FocusMinutes} min session";
        _playIco.Text = f.Running ? "" : "";
        _ring.Stroke = B(f.Running ? "#FFFFFF" : "#55FFFFFF");
    }
    void UpdateWeather()
    {
        if (_temp == null) return;
        var w = Svc.Weather;
        if (!w.Ready)
        {
            _temp.Text = "—"; _cond.Text = string.IsNullOrEmpty(Settings.Current.City) ? "Set your city" : (w.Error.Length > 0 ? "Offline" : "Loading"); _hl.Text = "";
            return;
        }
        _temp.Text = $"{Math.Round(w.Temp)}°"; _cond.Text = w.Condition; _wIco.Text = w.Glyph;
        _hl.Text = $"H {Math.Round(w.High)}°  L {Math.Round(w.Low)}°";
    }
    public override void Shown() { if (_built != DateTime.Today && _day != null) Calendar(); _ = Svc.Weather.Refresh(); }
    public override void Tick()
    {
        if (_cpu == null) return;
        var s = Svc.Stats; s.Sample();
        _cpu.Text = $"{s.Cpu * 100:0}%"; _ram.Text = $"{s.Ram * 100:0}%"; _cpuBar.Value = s.Cpu; _ramBar.Value = s.Ram;
        s.SampleGpu();
        _gpu.Text = s.Gpu < 0 ? "n/a" : $"{s.Gpu * 100:0}%"; _gpuBar.Value = Math.Max(0, s.Gpu);
        _net.Text = $"↓ {Stats.Rate(s.DownBps)}   ↑ {Stats.Rate(s.UpBps)}";
    }
}

// ───────────────────────── Controls ─────────────────────────
public class ControlsTab : TabBase
{
    public override string Id => "controls"; public override string Name => "Controls"; public override string Glyph => "";
    public override double Height => 238;
    MiniSlider _vol, _bri; TextBlock _volPct, _briPct, _muteSub, _soundSub, _wifiSub, _btSub, _nightSub, _focusSub; Grid _briGrid; Border _muteTile; Segmented _power;

    static Border Tile(string glyph, string title, TextBlock sub, Action click, string bg = Card, string? settings = null)
    {
        var badge = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = B("#33FFFFFF"), Child = Ico(glyph, 11), HorizontalAlignment = HorizontalAlignment.Left };
        var b = Btn(Col(badge, T(title, 11, "#FFFFFF", true).M(0, 3), sub), click, bg, 12, new Thickness(9, 7, 9, 7));
        if (settings != null) { b.ToolTip = "Click to switch · right-click for Windows settings"; b.MouseRightButtonUp += (_, e) => { Svc.Open(settings); e.Handled = true; }; }
        return b;
    }

    protected override FrameworkElement Build()
    {
        _vol = new MiniSlider(46, true, "#FFFFFF", "#1FFFFFFF", 13);
        _vol.Changed += v => { Svc.Speaker.Volume = v; _volPct.Text = $"{v * 100:0}%"; };
        _volPct = T("", 10, Dim); _volPct.HorizontalAlignment = HorizontalAlignment.Center; _volPct.VerticalAlignment = VerticalAlignment.Top; _volPct.IsHitTestVisible = false;
        var volIco = Ico("", 13, "#8E8E93"); volIco.VerticalAlignment = VerticalAlignment.Bottom; volIco.Margin = new Thickness(0, 0, 0, 8); volIco.IsHitTestVisible = false;
        var volGrid = new Grid { Width = 46 }; volGrid.Children.Add(_vol); volGrid.Children.Add(_volPct.M(0, 7)); volGrid.Children.Add(volIco);
        _bri = new MiniSlider(46, true, "#FFFFFF", "#1FFFFFFF", 13);
        _bri.Changed += v => { Brightness.Set(v); _briPct.Text = $"{v * 100:0}%"; };
        _briPct = T("n/a", 10, Dim); _briPct.HorizontalAlignment = HorizontalAlignment.Center; _briPct.VerticalAlignment = VerticalAlignment.Top; _briPct.IsHitTestVisible = false;
        var briIco = Ico("\uE706", 13, "#8E8E93"); briIco.VerticalAlignment = VerticalAlignment.Bottom; briIco.Margin = new Thickness(0, 0, 0, 8); briIco.IsHitTestVisible = false;
        _briGrid = new Grid { Width = 46, ToolTip = "Screen brightness" }; _briGrid.Children.Add(_bri); _briGrid.Children.Add(_briPct.M(0, 7)); _briGrid.Children.Add(briIco);
        _bri.IsHitTestVisible = false; _briGrid.Opacity = 0.5;

        _muteSub = T("", 10.5, "#C7C7CC"); _soundSub = T("", 10.5, "#C7C7CC"); _wifiSub = T("…", 10.5, "#C7C7CC"); _btSub = T("…", 10.5, "#C7C7CC"); _nightSub = T("…", 10.5, "#C7C7CC"); _focusSub = T("…", 10.5, "#C7C7CC");
        _soundSub.TextTrimming = TextTrimming.CharacterEllipsis;
        _muteTile = Tile("", "Mute", _muteSub, () => { Svc.Speaker.Muted = !Svc.Speaker.Muted; Tick(); });
        var tiles = MakeGrid("*,6,*,6,*", "*,6,*");
        tiles.Children.Add(Tile("", "Wi-Fi", _wifiSub, async () => { _wifiSub.Text = Quick.Label(await Quick.ToggleRadio(Windows.Devices.Radios.RadioKind.WiFi)); }, Card, "ms-settings:network-wifi").At(0, 0));
        tiles.Children.Add(Tile("", "Bluetooth", _btSub, async () => { _btSub.Text = Quick.Label(await Quick.ToggleRadio(Windows.Devices.Radios.RadioKind.Bluetooth)); }, Card, "ms-settings:bluetooth").At(0, 2));
        tiles.Children.Add(Tile("", "Night light", _nightSub, () => _nightSub.Text = Quick.Label(Quick.ToggleNightLight()), Card, "ms-settings:nightlight").At(0, 4));
        tiles.Children.Add(Tile("", "Do not disturb", _focusSub, () => _focusSub.Text = Quick.Label(Quick.ToggleDnd()), Card, "ms-settings:notifications").At(2, 0));
        tiles.Children.Add(_muteTile.At(2, 2));
        tiles.Children.Add(Tile("", "Sound", _soundSub, () => { Quick.NextOutput(); _soundSub.Text = Svc.Speaker.Name; }, Card, "ms-settings:sound").At(2, 4));

        _power = new Segmented(new[] { "Saver", "Balanced", "Performance" }, 1, i =>
        {
            if (!Native.SetPlan(i)) { Svc.Notices.Add("Power plan not switched", "Windows doesn't offer that plan on this PC.", ""); _power.Selected = Math.Max(0, Native.ActivePlan()); }
        }, true);

        var top = MakeGrid("auto,6,auto,8,*", "*");
        top.Children.Add(_briGrid.At(0, 0)); top.Children.Add(volGrid.At(0, 2)); top.Children.Add(tiles.At(0, 4));
        var g = MakeGrid("*", "*,6,auto");
        g.Children.Add(top.At(0, 0)); g.Children.Add(_power.At(2, 0));
        g.Margin = new Thickness(12, 4, 12, 12);
        return g;
    }
    public override void Shown()
    {
        if (_power == null) return;
        int p = Native.ActivePlan(); if (p >= 0) _power.Selected = p;
        _soundSub.Text = Svc.Speaker.Name;
        Tick();
        LoadBrightness();
        LoadStates();
    }
    async void LoadStates()
    {
        _wifiSub.Text = Quick.Label(await Quick.RadioOn(Windows.Devices.Radios.RadioKind.WiFi));
        _btSub.Text = Quick.Label(await Quick.RadioOn(Windows.Devices.Radios.RadioKind.Bluetooth));
        _nightSub.Text = Quick.Label(Quick.NightLightOn); _focusSub.Text = Quick.Label(Quick.DndOn);
    }
    async void LoadBrightness()
    {
        var b = await Brightness.Get();
        if (_bri.Dragging) return;
        bool ok = b != null;
        _bri.IsHitTestVisible = ok; _briGrid.Opacity = ok ? 1 : 0.5;
        _briGrid.ToolTip = ok ? "Screen brightness" : "This screen doesn't accept brightness commands";
        _bri.Value = b ?? 0; _briPct.Text = ok ? $"{b * 100:0}%" : "n/a";
    }
    public override void Tick()
    {
        if (_vol == null) return;
        if (!_vol.Dragging) { _vol.Value = Svc.Speaker.Volume; _volPct.Text = $"{_vol.Value * 100:0}%"; }
        bool m = Svc.Speaker.Muted;
        _muteSub.Text = m ? "Muted" : "Sound on";
        _muteTile.Opacity = 1;
    }
}

// ───────────────────────── Tools & launcher ─────────────────────────
public class ToolsTab : TabBase
{
    public override string Id => "tools"; public override string Name => "Tools"; public override string Glyph => "";
    public override double Height => 162;
    Grid _slots;

    static Border Big(string glyph, string text, Action a, string color = "#FFFFFF")
        => Btn(Row(Ico(glyph, 12, color), T(text, 11.5, "#FFFFFF", true).M(7)).With(r => r.HorizontalAlignment = HorizontalAlignment.Center), a, Card, 12, new Thickness(8, 9, 8, 9));

    protected override FrameworkElement Build()
    {
        var top = MakeGrid("*,6,*,6,*,6,*", "auto");
        top.Children.Add(Big("", "Screenshot", Shot).At(0, 0));
        top.Children.Add(Big("", "Snip", () => Svc.Open("ms-screenclip:")).At(0, 2));
        top.Children.Add(Big("", "Record", () => Rec.Toggle(), "#FF453A").At(0, 4));
        top.Children.Add(Big("\uE722", "Camera", () => CameraWindow.Toggle()).At(0, 6));
        _slots = MakeGrid("*,*,*,*,*,*", "auto");
        AppPicker.Warm(); // builds the list of programs in the background so the picker opens ready
        Fill();
        return Col(top, _slots.M(0, 12)).M(12, 4, 12, 0);
    }
    void Shot() => Perch.Shot.Start("area");
    void Fill()
    {
        _slots.Children.Clear();
        var slots = Settings.Current.Slots;
        while (slots.Count < 6) slots.Add("");
        for (int i = 0; i < 6; i++)
        {
            int idx = i; var path = slots[i];
            bool has = path.Length > 0 && File.Exists(path);
            UIElement inner = has ? new Image { Source = Native.FileIcon(path), Width = 26, Height = 26 } : Ico("", 12, "#C7C7CC");
            var tile = Btn(inner, () =>
            {
                if (has) { Svc.Open(path); return; }
                // choose from a searchable list of the programs on this PC
                AppPicker.Choose(p => { slots[idx] = p; Settings.Current.Save(); Fill(); });
            }, has ? Card : "#00FFFFFF", 12, new Thickness(0));
            tile.Width = tile.Height = 44;
            if (!has) { tile.BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)); tile.BorderThickness = new Thickness(1); }
            else { tile.ToolTip = System.IO.Path.GetFileNameWithoutExtension(path) + "\nRight-click to clear"; tile.MouseRightButtonUp += (_, _) => { slots[idx] = ""; Settings.Current.Save(); Fill(); }; }
            var label = T(has ? System.IO.Path.GetFileNameWithoutExtension(path) : "Add", 10, Dim); label.HorizontalAlignment = HorizontalAlignment.Center; label.MaxWidth = 56;
            _slots.Children.Add(Col(tile, label.M(0, 4)).At(0, i));
        }
    }
}

// ───────────────────────── Phone (not built) ─────────────────────────
public class PhoneTab : TabBase
{
    public override string Id => "phone"; public override string Name => "Phone"; public override string Glyph => "";
    public override double Height => 150;
    protected override FrameworkElement Build()
    {
        var body = T("Using a phone as a remote for this PC isn't part of Perch yet. Scripts on this PC can already post to the island through the notify endpoint; see Settings, Panels.", 11, "#C7C7CC");
        body.TextWrapping = TextWrapping.Wrap;
        return Col(T("Phone remote", 12.5, "#FFFFFF", true), body.M(0, 4), Pill("Open settings", () => SettingsWindow.Open("Panels")).With(p => p.HorizontalAlignment = HorizontalAlignment.Left).M(0, 10)).M(16, 6, 16, 0);
    }
}

// ───────────────────────── Call ─────────────────────────
public class CallTab : TabBase
{
    public override string Id => "call"; public override string Name => "Call"; public override string Glyph => "";
    public override double Height => 172;
    TextBlock _micIco, _micTitle, _clock, _spk, _mic, _startText; Border _micBtn, _live; DateTime? _since;

    protected override FrameworkElement Build()
    {
        _micIco = Ico("", 20);
        _micBtn = Btn(_micIco, () => { Svc.Mic.Muted = !Svc.Mic.Muted; Tick(); }, "#2E5A46", 31, new Thickness(0), "#3A6E57");
        _micBtn.Width = _micBtn.Height = 62;
        _micTitle = T("Mic on", 11.5, "#FFFFFF", true); _micTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var sub = T("Click to mute", 10.5, Dim); sub.HorizontalAlignment = HorizontalAlignment.Center;
        var left = Col(_micBtn, _micTitle.M(0, 7), sub); left.Width = 110; left.VerticalAlignment = VerticalAlignment.Center;

        _clock = T("0:00", 22, "#FFFFFF", true, true);
        _live = new Border { CornerRadius = new CornerRadius(8), Background = B("#1F8A4C"), Padding = new Thickness(7, 2, 7, 2), Child = T("Live", 10, "#FFFFFF", true), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        _startText = T("Start timer", 11, "#FFFFFF", true);
        var start = Btn(_startText, () => { _since = _since == null ? DateTime.Now : null; Tick(); }, Card, 8, new Thickness(10, 4, 10, 4));
        start.VerticalAlignment = VerticalAlignment.Center;
        _spk = T("", 11, "#FFFFFF", true); _mic = T("", 11, "#FFFFFF", true);
        Border Line(string glyph, TextBlock name, Action open)
        {
            var g = MakeGrid("auto,*,auto", "auto");
            g.Children.Add(Ico(glyph, 12, "#D1D1D6").At(0, 0)); g.Children.Add(name.M(8, 0, 6).At(0, 1));
            g.Children.Add(Ico("", 8, Dim).At(0, 2));
            return Btn(g, open, Card, 9, new Thickness(10, 6, 10, 6));
        }
        var right = Col(
            Row(_clock, _live.M(8), start.M(8)),
            Line("", _spk, () => Svc.Open("ms-settings:sound")).M(0, 6),
            Line("", _mic, () => Svc.Open("ms-settings:sound")).M(0, 5),
            Pill("Check camera", () => Svc.Open("microsoft.windows.camera:"), false, "").With(p => p.HorizontalAlignment = HorizontalAlignment.Left).M(0, 5));
        var g2 = MakeGrid("auto,*", "*");
        g2.Children.Add(left.At(0, 0)); g2.Children.Add(right.M(8, 0, 0, 0).At(0, 1));
        g2.Margin = new Thickness(12, 2, 14, 0);
        return g2;
    }
    public override void Shown() { if (_spk == null) return; _spk.Text = Svc.Speaker.Name; _mic.Text = Svc.Mic.Name; Tick(); }
    public override void Tick()
    {
        if (_micIco == null) return;
        bool muted = Svc.Mic.Muted;
        _micIco.Text = muted ? "" : "";
        _micTitle.Text = muted ? "Mic muted" : "Mic on";
        _micBtn.Background = B(muted ? "#5A2E2E" : "#2E5A46");
        _clock.Text = _since == null ? "0:00" : Fmt(DateTime.Now - _since.Value);
        _live.Visibility = _since == null ? Visibility.Collapsed : Visibility.Visible;
        _startText.Text = _since == null ? "Start timer" : "Stop";
    }
}

public static class FluentExt
{
    public static T With<T>(this T e, Action<T> a) { a(e); return e; }
}

// The Controls tiles switch things in place instead of opening Settings.
public static class Quick
{
    public static string Label(bool? on) => on == null ? "Unavailable" : on.Value ? "On" : "Off";

    static async System.Threading.Tasks.Task<Windows.Devices.Radios.Radio?> Find(Windows.Devices.Radios.RadioKind k)
    {
        try { await Windows.Devices.Radios.Radio.RequestAccessAsync(); return (await Windows.Devices.Radios.Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == k); } catch { return null; }
    }
    public static async System.Threading.Tasks.Task<bool?> RadioOn(Windows.Devices.Radios.RadioKind k) { var r = await Find(k); return r == null ? null : r.State == Windows.Devices.Radios.RadioState.On; }
    public static async System.Threading.Tasks.Task<bool?> ToggleRadio(Windows.Devices.Radios.RadioKind k)
    {
        var r = await Find(k); if (r == null) return null;
        var to = r.State == Windows.Devices.Radios.RadioState.On ? Windows.Devices.Radios.RadioState.Off : Windows.Devices.Radios.RadioState.On;
        try { return await r.SetStateAsync(to) == Windows.Devices.Radios.RadioAccessStatus.Allowed ? to == Windows.Devices.Radios.RadioState.On : r.State == Windows.Devices.Radios.RadioState.On; } catch { return null; }
    }

    // shortcut: Night light has no public API; this flips the state blob Settings itself writes. If a Windows update changes the format, the tile shows Unavailable.
    const string NightKey = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";
    static byte[]? NightData() { try { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(NightKey); return k?.GetValue("Data") as byte[]; } catch { return null; } }
    public static bool? NightLightOn => NightData() is { Length: > 24 } d ? d[18] == 0x15 : null;
    public static bool? ToggleNightLight()
    {
        var d = NightData(); if (d is not { Length: > 24 }) return null;
        bool on = d[18] == 0x15; var l = d.ToList();
        if (on) { if (l[23] == 0x10 && l[24] == 0x00) l.RemoveRange(23, 2); l[18] = 0x13; }
        else { l.InsertRange(23, new byte[] { 0x10, 0x00 }); l[18] = 0x15; }
        for (int i = 10; i < 15; i++) if (l[i] != 0xFF) { l[i]++; break; } // a newer timestamp makes Windows apply it
        try { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(NightKey, true); k?.SetValue("Data", l.ToArray(), Microsoft.Win32.RegistryValueKind.Binary); } catch { return null; }
        return NightLightOn;
    }

    // Do not disturb: the same switch as Settings, Notifications (banners off, they still collect in the notification centre).
    const string DndKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    public static bool? DndOn { get { try { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(DndKey); return k?.GetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED") is int v && v == 0; } catch { return null; } } }
    public static bool? ToggleDnd()
    {
        try { bool to = DndOn != true; using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(DndKey); k.SetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED", to ? 0 : 1, Microsoft.Win32.RegistryValueKind.DWord); return DndOn; } catch { return null; }
    }

    public static void NextOutput()
    {
        var outs = Mixer.Outputs(); if (outs.Count < 2) { Svc.Notices.Add("Only one sound output", "Connect headphones or speakers to switch.", "\uE7F5"); return; }
        int at = Math.Max(0, outs.FindIndex(o => o.Default)), next = (at + 1) % outs.Count;
        bool ok = Mixer.SetOutput(outs[next].Id);
        Svc.Notices.Add(ok ? "Sound output changed" : "Could not switch output", outs[next].Name, "\uE7F5");
    }
}
