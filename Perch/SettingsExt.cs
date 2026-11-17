using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using static Perch.UI;

namespace Perch;

// The settings pages for the features added in the second port: windows, display, capture and so on.
public partial class SettingsWindow
{
    static void Native_(string uri) { try { Svc.Open(uri); } catch { } }

    void BuildExt(string name)
    {
        switch (name)
        {
            case "Windows": WindowsPage(); break;
            case "Desktop": DesktopPage(); break;
            case "Utilities": UtilitiesPage(); break;
            case "Capture": CapturePage(); break;
            case "Display": DisplayPage(); break;
            case "Alerts": AlertsPage(); break;
            case "More tabs": MoreTabsPage(); break;
            case "Features": FeaturesPage(); break;
            case "Updates": UpdatesPage(); break;
            case "Language": LanguagePage(); break;
        }
    }

    void WindowsPage()
    {
        Section("Window switcher");
        RowItem("Use Perch's switcher for Alt+Tab", "One card per window with a live preview. Hold Alt and press Tab to move, release Alt to switch, Shift+Tab goes back, type to search, Delete closes the selected window, Esc cancels. A quick tap switches straight to the last window without showing the list. Needs the keyboard hook, which Perch turns on only while this is on.",
            new Toggle(S.SwitcherOn, v => { S.SwitcherOn = v; InputCommit(); Render(); }));
        if (S.SwitcherOn)
        {
            RowItem("What the cards look like", "Previews show the window itself, icons are compact, the list is plain.",
                new Segmented(new[] { "Previews", "Icons", "List" }, S.SwitcherLayout == "icons" ? 1 : S.SwitcherLayout == "list" ? 2 : 0, i => { S.SwitcherLayout = new[] { "previews", "icons", "list" }[i]; S.Save(); }));
            RowItem("Minimised windows", "Show windows that are minimised to the taskbar.", new Toggle(S.SwitcherMinimized, v => { S.SwitcherMinimized = v; S.Save(); }));
            RowItem("Only this display", "List just the windows on the display the pointer is on.", new Toggle(S.SwitcherCurrentDisplay, v => { S.SwitcherCurrentDisplay = v; S.Save(); }));
            RowItem("Cycle one program's windows", "Alt+` (the key above Tab) steps through the windows of the program in front.", new Toggle(S.SwitcherSameApp, v => { S.SwitcherSameApp = v; InputCommit(); }));
            RowItem("Cards per row", "At most this many across.", SliderRow(S.SwitcherColumns, 2, 8, "", v => { S.SwitcherColumns = (int)v; }));
            RowItem("Wait before showing", "A tap shorter than this switches without ever showing the list.", SliderRow(S.SwitcherDelayMs, 0, 500, "ms", v => { S.SwitcherDelayMs = (int)v; }));
            AppList("Never list these programs", "They are skipped in the switcher.", S.SwitcherHiddenApps, () => S.Save());
            RowItem("Open the switcher from a shortcut", "For when you would rather not use Alt+Tab. Set it under Keys.", Pill("Open Keys", () => Go("Keys")));
        }
        Section("Window layouts");
        RowItem("Shortcuts", "Halves, thirds, two thirds, quarter rows and columns, sixths, corners, centre, maximise with margin, next and previous display, and put it back. Each has a shortcut you can set under Keys.", Pill("Open Keys", () => Go("Keys")));
        RowItem("Window gap", "Space between snapped windows, in pixels.", SliderRow(S.LayoutGap, 0, 40, "px", v => { S.LayoutGap = (int)v; }));
        RowItem("Screen gap", "Space between a snapped window and the screen edge.", SliderRow(S.LayoutScreenGap, 0, 40, "px", v => { S.LayoutScreenGap = (int)v; }));
        RowItem("Margin for maximise with margin", "How much of the display is left around the window.", SliderRow(S.LayoutMarginPct, 0, 25, "%", v => { S.LayoutMarginPct = (int)v; }));
        RowItem("Repeat left or right to cycle sizes", "Half, then two thirds, then one third on the same display.", new Toggle(S.LayoutSideCycle, v => { S.LayoutSideCycle = v; S.Save(); }));
        AppList("Leave these programs alone", "Layout shortcuts and drag gestures do nothing to their windows.", S.LayoutIgnoredApps, InputCommit);
        RowItem("Move the pointer to the next display", "A shortcut puts the pointer in the middle of the next display. Set it under Keys.", Pill("Open Keys", () => Go("Keys")));
        Section("Drag to move and resize");
        RowItem("Hold a key and drag anywhere in a window", "Left-drag moves it. Add Shift, or use the right button, to resize from the nearest edge or corner. Perch watches the pointer only while this is on.",
            new Toggle(S.GestureOn, v => { S.GestureOn = v; InputCommit(); Render(); }));
        if (S.GestureOn)
        {
            RowItem("The key", "Alt is the usual choice.", new Segmented(new[] { "Alt", "Ctrl+Alt", "Windows" }, S.GestureKey == "ctrlalt" ? 1 : S.GestureKey == "win" ? 2 : 0, i => { S.GestureKey = new[] { "alt", "ctrlalt", "win" }[i]; InputCommit(); }));
            RowItem("Bring the window to the front", "The window you drag comes forward.", new Toggle(S.GestureRaise, v => { S.GestureRaise = v; InputCommit(); }));
            RowItem("Snap at screen edges", "Let go at the edge of the display and the window snaps: sides to a half, the top to full, corners to a quarter.", new Toggle(S.GestureEdgeSnap, v => { S.GestureEdgeSnap = v; InputCommit(); }));
        }
        Section("Switch desktops with a mouse button");
        RowItem("Hold a button and drag", "Drag sideways to switch virtual desktops, up for all windows, down to show the desktop. A short click still does what it did.",
            new Toggle(S.DeskGesture, v => { S.DeskGesture = v; InputCommit(); Render(); }));
        if (S.DeskGesture)
        {
            RowItem("The button", "", new Segmented(new[] { "Middle", "Back", "Forward" }, S.DeskGestureButton == "x1" ? 1 : S.DeskGestureButton == "x2" ? 2 : 0, i => { S.DeskGestureButton = new[] { "middle", "x1", "x2" }[i]; InputCommit(); }));
            RowItem("Desktops follow the drag", "Dragging right brings the desktop on the left, like a swipe on a touchpad.", new Toggle(S.DeskFollow, v => { S.DeskFollow = v; InputCommit(); }));
        }
        Section("Already in Windows");
        RowItem("Snap layouts and edge snapping", "Dragging a window to an edge, and the layout picker on the maximise button, are Windows' own.", Pill("Open", () => Native_("ms-settings:multitasking")));
        RowItem("Taskbar clicks and previews", "Clicking an open program's taskbar icon minimises or cycles it, and hovering shows its windows.", Pill("Open", () => Native_("ms-settings:taskbar")));
        RowItem("Touchpad gestures", "Three and four finger taps and swipes, including a middle click.", Pill("Open", () => Native_("ms-settings:devices-touchpad")));
        RowItem("Mouse buttons and scrolling", "Back and Forward buttons already work in Explorer and browsers.", Pill("Open", () => Native_("ms-settings:mousetouchpad")));
    }
    void UtilitiesPage()
    {
        Section("Clipboard");
        RowItem("Skip text that looks sensitive", "Passwords, tokens and keys are not saved to the history. It can skip ordinary text that resembles one. Anything a password manager marks private is always skipped.", new Toggle(S.ClipSkipSensitive, v => { S.ClipSkipSensitive = v; S.Save(); }));
        RowItem("Clear the clipboard when the screen locks", "The system clipboard is emptied. What is already in the history stays there.", new Toggle(S.ClipClearOnLock, v => { S.ClipClearOnLock = v; S.Save(); }));
        RowItem("Clear the clipboard when the PC sleeps", "The same, on sleep.", new Toggle(S.ClipClearOnSleep, v => { S.ClipClearOnSleep = v; S.Save(); }));
        AppList("Programs whose copies are never saved", "A password manager, say.", S.ClipIgnoredApps, () => S.Save());
        RowItem("History window", "A window with search over what you copied. Enter pastes a row; Ctrl+1 to Ctrl+9 paste that row. Set the shortcut under Keys.", Pill("Open Keys", () => Go("Keys")));
        Section("Text snippets menu");
        RowItem("Snippet menu", "A searchable menu of your snippets. The one you pick is typed where your cursor is. Set the shortcut under Keys, or open it now.", Row(Pill("Open", () => QuickPicks.Snippets()), Pill("Open Keys", () => Go("Keys")).M(6)));
        foreach (var sn in S.TextSnippets.ToList())
        {
            var snip = sn;
            var nm = Field(snip.Name, "Name", 120, t => { snip.Name = t; InputCommit(); }, out var tb1); tb1.LostFocus += (_, _) => { snip.Name = tb1.Text.Trim(); S.Save(); };
            var fo = Field(snip.Folder, "Folder", 90, t => { snip.Folder = t; InputCommit(); }, out var tb2); tb2.LostFocus += (_, _) => { snip.Folder = tb2.Text.Trim(); S.Save(); };
            RowItem((snip.Name.Length > 0 ? snip.Name : snip.Trigger.Length > 0 ? snip.Trigger : "Snippet"), snip.Text.Length > 70 ? snip.Text[..70] + "…" : snip.Text,
                Row(nm.M(0, 0, 6), fo.M(0, 0, 6), Row(T("In menu", 11, Dim).M(0, 0, 6).With(x => x.VerticalAlignment = VerticalAlignment.Center), new Toggle(snip.InLibrary, v => { snip.InLibrary = v; S.Save(); }))), true);
        }
        RowItem("Variables", "{{date}}, {{time}}, {{datetime}} and {{clipboard}} fill in when a snippet is typed. A format after a colon sets how: {{date:yyyy-MM-dd}}. A time zone goes before it: {{date-tz(Eastern Standard Time):HH:mm}}.", null);
        Section("Colour picker");
        string[] fmts = { "hex", "rgb", "hsl", "css", "csharp" };
        RowItem("Copy colours as", "How a colour from the picker is written when it is copied.", Chips(new[] { "#RRGGBB", "rgb()", "hsl()", "#rrggbb", "C# Color" }, Math.Max(0, Array.IndexOf(fmts, S.ColourFormat)), i => { S.ColourFormat = fmts[i]; S.Save(); }), true);
        Section("Closing programs");
        AppList("Programs Quit others always leaves running", "The Quit others tile and its shortcut ask every other program to close. These are skipped. Explorer always is.", S.QuitKept, () => S.Save());
        Section("Music apps");
        RowItem("Keep music apps from opening", "Pressing a play or pause key while no music is playing can wake a music app you did not want. With this on, a listed app that starts within a few seconds of the play key is closed again. Perch listens only to that one key.",
            new Toggle(S.MusicBlock, v => { S.MusicBlock = v; InputCommit(); Render(); }));
        if (S.MusicBlock) AppList("Apps to keep closed", "Their program file names.", S.MusicBlockApps, () => S.Save());
    }
    void CapturePage()
    {
        Section("Screenshot");
        RowItem("Take one", "Drag an area, or click a window, on a frozen picture of the screen. Enter takes the whole display, R repeats the last area, S turns on scrolling capture. Set the shortcuts under Keys.", Row(Pill("Area or window", () => { Close(); Shot.Start("area"); }, true), Pill("Whole display", () => { Close(); Shot.Start("full"); }).M(6)));
        string[] acts = { "editor", "savecopy", "copy", "save" };
        RowItem("What happens next", "Open the editor to mark it up, or just save, copy, or both.", Chips(new[] { "Open the editor", "Save and copy", "Copy", "Save" }, Math.Max(0, Array.IndexOf(acts, S.ShotAction)), i => { S.ShotAction = acts[i]; S.Save(); }), true);
        RowItem("Save to", string.IsNullOrEmpty(S.ShotFolder) ? "Pictures\\Perch" : S.ShotFolder, Row(Pill("Choose…", () => { using var dlg = new System.Windows.Forms.FolderBrowserDialog(); if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) { S.ShotFolder = dlg.SelectedPath; S.Save(); Render(); } }), Pill("Default", () => { S.ShotFolder = ""; S.Save(); Render(); }).M(6)));
        RowItem("File name", "%y year, %year, %mo month, %month name, %d day, %h hour, %mi minute, %s second, %# a running number (%## pads to 2 digits).",
            Field(S.ShotPattern, "Screenshot %y-%mo-%d %h.%mi.%s", 280, t => { S.ShotPattern = t.Length > 0 ? t : "Screenshot %y-%mo-%d %h.%mi.%s"; S.Save(); }, out _), true);
        RowItem("Subfolder", "Optional. Dated subfolders inside the folder above, with the same tokens, like %y-%mo.", Field(S.ShotSubfolder, "%y-%mo", 180, t => { S.ShotSubfolder = t; S.Save(); }, out _), true);
        RowItem("Delay", "Seconds to wait before the screen is read.", SliderRow(S.ShotDelay, 0, 10, "s", v => { S.ShotDelay = (int)v; }));
        RowItem("Include the pointer", "The mouse pointer is drawn into whole-display captures.", new Toggle(S.ShotPointer, v => { S.ShotPointer = v; S.Save(); }));
        RowItem("Add saved captures to the shelf", "Every saved capture also goes on the island's shelf, ready to drag into any app.", new Toggle(S.ShotAddToShelf, v => { S.ShotAddToShelf = v; S.Save(); }));
        RowItem("Scrolling capture scrolls by itself", "Perch scrolls the chosen area and stops when no new content appears. Off, you scroll it yourself.", new Toggle(S.ShotScrollAuto, v => { S.ShotScrollAuto = v; S.Save(); }));
        RowItem("Watermark", "Text drawn in the corner of what you save or copy from the editor.", Field(S.ShotWatermark, "Your name or site", 200, t => { S.ShotWatermark = t; S.Save(); }, out _), true);
        Section("Screen recording");
        RowItem("Record", "Pick an area or a window, and Perch records it. A small bar shows the time with Pause and Stop. When it stops you can trim it and save a video or a GIF.", Row(Pill("Record now", () => { Close(); Rec.Toggle(); }, true), Pill("Open Keys", () => Go("Keys")).M(6)));
        RowItem("Countdown", "Seconds before recording starts.", SliderRow(S.RecCountdown, 0, 10, "s", v => { S.RecCountdown = (int)v; }));
        RowItem("Frames per second", "More is smoother and makes bigger files.", Segmented5(new[] { 10, 15, 24, 30 }, S.RecFps, v => { S.RecFps = v; S.Save(); }));
        string[] qs = { "small", "balanced", "high" };
        RowItem("Quality", "Small keeps files tiny; high keeps every detail.", new Segmented(new[] { "Small file", "Balanced", "High" }, Math.Max(0, Array.IndexOf(qs, S.RecQuality)), i => { S.RecQuality = qs[i]; S.Save(); }));
        RowItem("Record the sound of the PC", "What you hear goes into the recording on its own track.", new Toggle(S.RecSystemAudio, v => { S.RecSystemAudio = v; S.Save(); }));
        RowItem("Record the microphone", "Your voice goes into the recording too.", new Toggle(S.RecMic, v => { S.RecMic = v; S.Save(); }));
        RowItem("Draw the pointer", "The pointer is visible in the recording.", new Toggle(S.RecPointer, v => { S.RecPointer = v; S.Save(); }));
        RowItem("Save to", string.IsNullOrEmpty(S.RecFolder) ? "Videos\\Perch" : S.RecFolder, Row(Pill("Choose…", () => { using var dlg = new System.Windows.Forms.FolderBrowserDialog(); if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) { S.RecFolder = dlg.SelectedPath; S.Save(); Render(); } }), Pill("Default", () => { S.RecFolder = ""; S.Save(); Render(); }).M(6)));
        RowItem("GIF smoothness", "Frames per second in a saved GIF.", SliderRow(S.RecGifFps, 5, 20, "fps", v => { S.RecGifFps = (int)v; }));
        Section("Camera mirror");
        RowItem("Open the mirror", "A floating mirror to check how you look before a call. The camera runs only while it is open and nothing is recorded. Set the shortcut under Keys.", Row(Pill("Open", () => CameraWindow.Toggle(), true), Pill("Camera privacy", () => Native_("ms-settings:privacy-webcam")).M(6)));
        RowItem("Flip it like a mirror", "Left and right are swapped, as in a mirror.", new Toggle(S.CameraMirror, v => { S.CameraMirror = v; S.Save(); }));
    }
    static FrameworkElement Segmented5(int[] values, int current, Action<int> set)
        => new Segmented(values.Select(v => v + " fps").ToArray(), Math.Max(0, Array.IndexOf(values, current)), i => set(values[i]));
    void DisplayPage()
    {
        Section("Displays");
        var mons = Displays.List();
        RowItem("Brightness shortcuts", "Two shortcuts change the brightness of the display the pointer is on, in steps. Set them under Keys.", Row(SliderRow(S.DisplayStep, 2, 25, "%", v => { S.DisplayStep = (int)v; }), Pill("Open Keys", () => Go("Keys")).M(8)));
        foreach (var m in mons)
        {
            var mon = m; int dim = S.DisplayDim.TryGetValue(mon.Device, out var dv) ? dv : 100;
            RowItem(mon.Name + (mon.Primary ? "  (main)" : ""), mon.Ddc ? "Adjusted over the monitor's own control channel (DDC/CI), the same as its buttons." : "This display cannot be adjusted from the PC, so the slider dims the picture instead.", null);
            if (mon.Ddc) RowItem("Brightness", "", SliderRow((mon.Cur - mon.Min) * 100.0 / Math.Max(1, mon.Max - mon.Min), 0, 100, "%", v => { Displays.SetBrightness(mon, (int)v); }));
            RowItem("Dim the picture", "A dark layer over the whole display, for when even the lowest brightness is too bright, or the monitor cannot be adjusted. 100 is no dimming.", SliderRow(dim, 10, 100, "%", v => { Displays.Dim(mon.Device, (int)v); }));
            if (mon.Speakers) RowItem("Speaker volume", "This monitor has speakers of its own, adjusted over the same channel as its buttons.", Row(SliderRow(mon.Volume, 0, 100, "%", v => { Displays.SetVolume(mon, (int)v); }), Pill("Mute", () => Displays.SetMute(mon, true)).M(8), Pill("Unmute", () => Displays.SetMute(mon, false)).M(5)));
        }
        RowItem("Laptop screen", "The built-in panel is adjusted from the Controls tab and by the keyboard's own brightness keys.", Pill("Open Windows display settings", () => Native_("ms-settings:display")));
        RowItem("Night light and HDR", "Windows' own warm-colour schedule and HDR controls.", Pill("Open", () => Native_("ms-settings:nightlight")));
    }
    void AlertsPage()
    {
        Section("Warnings");
        RowItem("The processor is busy", "Says so when the load stays above the level for a minute.", new Toggle(S.AlertCpu, v => { S.AlertCpu = v; S.Save(); Render(); }));
        if (S.AlertCpu) RowItem("At or above", "", SliderRow(S.AlertCpuPct, 50, 100, "%", v => { S.AlertCpuPct = (int)v; }));
        RowItem("Memory is nearly full", "Says so when this much of the memory is in use.", new Toggle(S.AlertMem, v => { S.AlertMem = v; S.Save(); Render(); }));
        if (S.AlertMem) RowItem("At or above", "", SliderRow(S.AlertMemPct, 60, 99, "%", v => { S.AlertMemPct = (int)v; }));
        RowItem("The disk is nearly full", "Says so when the system drive has this little free space.", new Toggle(S.AlertDisk, v => { S.AlertDisk = v; S.Save(); Render(); }));
        if (S.AlertDisk) RowItem("At or below", "Free space.", SliderRow(S.AlertDiskPct, 2, 40, "%", v => { S.AlertDiskPct = (int)v; }));
        RowItem("Tell me again after", "A warning is not repeated sooner than this.", SliderRow(S.AlertCooldownMin, 5, 240, "min", v => { S.AlertCooldownMin = (int)v; }));
        RowItem("Accessory batteries", "Warns once when a paired Bluetooth accessory falls to 20 percent.", new Toggle(S.AccessoryAlerts, v => { S.AccessoryAlerts = v; S.Save(); }));
        Section("Keep awake, more rules");
        RowItem("While another display is connected", "No sleep while a second display is plugged in, like a docked laptop.", new Toggle(S.AwakeExternal, v => { S.AwakeExternal = v; S.Save(); }));
        RowItem("Pause the rules while the screen is locked", "Every keep-awake rule waits until you unlock.", new Toggle(S.AwakePauseLocked, v => { S.AwakePauseLocked = v; S.Save(); }));
        RowItem("Nudge the pointer", "While a keep-awake session or rule is active, the pointer moves one pixel and back now and then, so chat apps do not show you as away.", new Toggle(S.AwakeJiggle, v => { S.AwakeJiggle = v; S.Save(); Render(); }));
        if (S.AwakeJiggle) RowItem("How often", "", SliderRow(S.AwakeJiggleSec, 15, 300, "s", v => { S.AwakeJiggleSec = (int)v; }));
    }
    void MoreTabsPage()
    {
        Section("New tabs");
        RowItem("They start hidden", "The Timer, Calendar, Lyrics, Watch and AI agents tabs are off until you switch them on, so an update never grows the island by itself. Turn them on under Panels.", Pill("Open Panels", () => Go("Panels")));
        Section("Calendar");
        RowItem("Windows' own calendar", "Appointments from the accounts you added to the Windows Calendar app, when Windows allows it.", new Toggle(S.CalendarWindows, v => { S.CalendarWindows = v; S.Save(); _ = Cal.Refresh(true); }));
        foreach (var src in S.CalendarSources.ToList()) { var s2 = src; RowItem(s2.Length > 60 ? s2[..58] + "…" : s2, "", Pill("Remove", () => { S.CalendarSources.Remove(s2); S.Save(); _ = Cal.Refresh(true); Render(); })); }
        RowItem("Add a calendar file", "An .ics file saved from Google Calendar, Outlook or Apple Calendar.", Pill("Choose file…", () => { var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Calendar|*.ics" }; if (dlg.ShowDialog() == true) { S.CalendarSources.Add(dlg.FileName); S.Save(); _ = Cal.Refresh(true); Render(); } }));
        RowItem("Add a calendar link", "A private .ics address a calendar service gives you. It is fetched every ten minutes and kept only in memory.", Field("", "https://…/basic.ics", 260, t => { if (t.StartsWith("http", StringComparison.OrdinalIgnoreCase)) { S.CalendarSources.Add(t); S.Save(); _ = Cal.Refresh(true); Render(); } }, out _), true);
        Section("AI agents");
        RowItem("What Perch reads", "The session files Claude Code (.claude) and Codex (.codex) keep in your user folder: dates, model names, project folders and token counts. Prompts and replies are never kept. Nothing is written, sent or signed in to.", null);
        RowItem("Claude Code", AgentUsage.Found("Claude") ? "Found on this PC." : "Not found on this PC.", new Toggle(S.AgentsClaude, v => { S.AgentsClaude = v; S.Save(); }));
        RowItem("Codex", AgentUsage.Found("Codex") ? "Found on this PC." : "Not found on this PC.", new Toggle(S.AgentsCodex, v => { S.AgentsCodex = v; S.Save(); }));
        RowItem("Tell me when a task finishes", "A message when an agent that was working goes quiet.", new Toggle(S.AgentsFinishAlert, v => { S.AgentsFinishAlert = v; S.Save(); Render(); }));
        if (S.AgentsFinishAlert) RowItem("For tasks longer than", "", SliderRow(S.AgentsFinishMinutes, 0, 30, "min", v => { S.AgentsFinishMinutes = (int)v; }));
        RowItem("Daily budget", "Shows how much of a daily API value is left. 0 turns it off.", SliderRow(S.AgentsDailyBudget, 0, 200, "$", v => { S.AgentsDailyBudget = v; }));
        Section("Lyrics");
        RowItem("Find lyrics online", "While the Lyrics tab is open, the song title, artist and length are sent to lrclib.net. Imported lyrics stay on this PC.", new Toggle(S.LyricsOnline, v => { S.LyricsOnline = v; S.Save(); }));
        Section("Timer");
        RowItem("Play a sound when time is up", "", new Toggle(S.TimerSound, v => { S.TimerSound = v; S.Save(); }));
        RowItem("Pomodoro", "Focus " + S.PomoFocus + " min, short break " + S.PomoShort + " min, long break " + S.PomoLong + " min after every " + S.PomoEvery + " sessions, " + S.PomoTotal + " in all. Change focus and break lengths on the Timer tab.", null);
        RowItem("Long break", "Minutes.", SliderRow(S.PomoLong, 5, 60, "min", v => { S.PomoLong = (int)v; }));
        RowItem("Sessions between long breaks", "", SliderRow(S.PomoEvery, 2, 8, "", v => { S.PomoEvery = (int)v; }));
        RowItem("Total sessions", "", SliderRow(S.PomoTotal, 1, 16, "", v => { S.PomoTotal = (int)v; }));
    }
    void FeaturesPage()
    {
        Section("Start with a bundle");
        RowItem("Essentials", "Music, controls, mixer, clipboard, the utilities and nothing that listens to the keyboard or mouse.", Pill("Apply", () => Bundle("essentials")));
        RowItem("Windows", "Essentials plus the window switcher, the window layouts and drag gestures.", Pill("Apply", () => Bundle("windows")));
        RowItem("Battery and quiet", "Only music, notices and controls. Every keyboard and mouse hook is switched off.", Pill("Apply", () => Bundle("quiet")));
        RowItem("Everything", "Every tab visible. Input tweaks stay as you set them.", Pill("Apply", () => Bundle("all")));
        Section("What is listening");
        RowItem("Mouse", Hooks.ListeningMouse, null);
        RowItem("Keyboard", Hooks.ListeningKeys, null);
        RowItem("Idle cost", "Perch loads nothing for a feature that is off: the hooks stop, and a hidden tab builds no view.", null);
        Section("Tabs");
        foreach (var tab in TabBase.All())
        {
            var t2 = tab;
            RowItem(t2.Name, TabBlurb(t2.Id), new Toggle(!S.HiddenTabs.Contains(t2.Id), v => { if (v) S.HiddenTabs.Remove(t2.Id); else if (S.HiddenTabs.Count < TabBase.All().Count - 1) S.HiddenTabs.Add(t2.Id); S.Save(); IslandWindow.Instance.RefreshChrome(); }));
        }
    }
    void Bundle(string id)
    {
        var tabs = TabBase.All().Select(t => t.Id).ToList();
        var keep = id switch
        {
            "essentials" => new[] { "music", "notices", "clip", "controls", "mixer", "utils", "shelf", "sites", "tools", "notes" },
            "windows" => new[] { "music", "notices", "clip", "controls", "mixer", "utils", "shelf", "tools", "apps" },
            "quiet" => new[] { "music", "notices", "controls" },
            _ => tabs.ToArray()
        };
        S.HiddenTabs = tabs.Where(t => !keep.Contains(t)).ToList();
        if (id is "essentials" or "quiet") { S.SwitcherOn = false; S.GestureOn = false; S.DeskGesture = false; S.MusicBlock = false; }
        if (id == "windows") { S.SwitcherOn = true; S.GestureOn = true; }
        if (id == "quiet") { S.ScrollInvert = S.ScrollInvertH = S.ScrollSideways = S.ScrollLinear = S.ScrollSmooth = S.ClickFilter = S.KeyDebounce = S.QuitGuard = S.SnippetExpand = false; S.SuperKey = "off"; S.BtnBack = S.BtnForward = S.BtnMiddle = "default"; S.FocusFollowsMouse = false; }
        S.Save(); Hooks.Refresh(); IslandWindow.Instance.RefreshChrome(); Svc.Notices.Add("Bundle applied", "Nothing was deleted. Switch anything back on under Features.", ""); Render();
    }
    void LanguagePage()
    {
        Section("Language");
        RowItem("Show Perch in", "Perch's words are written in English. Pick another language and the assistant translates the words the screens use, with your own API key, into a file in %AppData%\\Perch\\lang. Only the interface words are sent, never anything of yours. Restart Perch afterwards to see it on the island too.",
            Chips(Loc.Languages, Math.Max(0, Array.IndexOf(Loc.Languages, S.Language)), i => { S.Language = Loc.Languages[i]; S.Save(); Loc.Load(); Render(); }), true);
        if (S.Language != "English")
        {
            RowItem("Translated so far", Loc.Has ? Loc.Count + " phrases." : "Nothing yet.", Pill(Loc.Has ? "Translate again" : "Translate now", async () =>
            {
                CollectStrings();
                var r = await Loc.Translate(Loc.Seen.ToList(), S.Language, m => Svc.Notices.Add("Translating", m, ""));
                Svc.Notices.Add(r.Error.Length > 0 ? "Translation stopped" : "Translation ready", r.Error.Length > 0 ? r.Error : r.Done + " phrases. Restart Perch to see it everywhere.", "");
                Loc.Load(); Render();
            }, true));
            RowItem("Restart Perch", "Applies the language to the island's tabs.", Pill("Restart", () => Loc.Restart()));
        }
        Section("Companion");
        RowItem("A little friend beside the island", "It rests when nothing happens, hops over when something does, and likes being petted. It uses no permission and reads nothing.", new Toggle(S.MascotOn, v => { S.MascotOn = v; S.Save(); Mascot.Sync(); Render(); }));
        if (S.MascotOn)
        {
            string[] shapes = { "ball", "egg", "pill", "square" };
            RowItem("Shape", "", Chips(new[] { "Ball", "Egg", "Pill", "Rounded square" }, Math.Max(0, Array.IndexOf(shapes, S.MascotShape)), i => { S.MascotShape = shapes[i]; S.Save(); Mascot.Sync(); }), true);
            string[] cols = { "#F2F2F7", "#B8F2E6", "#FFD8BE", "#D9C8FF", "#FFF3A8", "#FFC4D6" };
            RowItem("Colour", "", Chips(new[] { "Pearl", "Mint", "Peach", "Lilac", "Lemon", "Rose" }, Math.Max(0, Array.IndexOf(cols, S.MascotColour)), i => { S.MascotColour = cols[i]; S.Save(); Mascot.Sync(); }), true);
            RowItem("Side of the island", "", new Segmented(new[] { "Left", "Right" }, S.MascotSide == "right" ? 1 : 0, i => { S.MascotSide = i == 1 ? "right" : "left"; S.Save(); Mascot.Sync(); }));
            RowItem("Visit now and then", "Every few minutes it hops over to say hello.", new Toggle(S.MascotVisits, v => { S.MascotVisits = v; S.Save(); }));
            RowItem("Hide when idle", "After a minute of quiet it fades away until something happens.", new Toggle(S.MascotHidesIdle, v => { S.MascotHidesIdle = v; S.Save(); }));
        }
        Section("Remove Perch");
        RowItem("Remove Perch's data and startup entry", "Deletes %AppData%\\Perch (settings, clipboard history, extensions, translations), the start-with-Windows entry and the tray icon, then quits. The program file itself is yours to delete. Nothing else on this PC is touched.",
            Pill("Remove…", () =>
            {
                if (MessageBox.Show("Delete Perch's settings and data, and stop it starting with Windows?\n\nThe program file stays where it is.", "Perch", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                try { Native.SetStartup(false); } catch { }
                try { var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Perch"); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{dir}\"") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
                Application.Current.Shutdown();
            }));
    }
    // Builds every page and tab once, so the words they use are known to the translator.
    void CollectStrings()
    {
        var keep = _current; var kf = _filter; _filter = "";
        foreach (var (name, _) in Pages) { _page.Children.Clear(); try { BuildPage(name); } catch { } }
        _page.Children.Clear();
        foreach (var t in TabBase.All()) { try { _ = t.View; Loc.Seen.Add(t.Name); } catch { } }
        _current = keep; _filter = kf;
    }
    string _updMsg = "";
    void UpdatesPage()
    {
        Section("Updates");
        RowItem("This version", Updater.CurrentText + ". Updates come from the project's GitHub releases.", null);
        RowItem("Look for updates once a day", "Off by default. The only thing sent is the request for the latest release; nothing about you or this PC.", new Toggle(S.UpdateCheck, v => { S.UpdateCheck = v; S.Save(); }));
        RowItem("Check now", S.UpdateLastCheck.Length > 0 ? "Last checked " + S.UpdateLastCheck + "." : "Not checked yet.",
            Pill("Check", async () => { _updMsg = "Checking…"; Render(); _updMsg = await Updater.Check(true); Render(); }, true));
        if (_updMsg.Length > 0) RowItem(_updMsg, "", null);
        var info = Updater.Available;
        if (info != null)
        {
            RowItem("Version " + info.Version, info.Notes.Length > 600 ? info.Notes[..600] + "…" : (info.Notes.Length > 0 ? info.Notes : "No release notes."),
                Row(Pill("Update now", async () => { var r = await Updater.Install(m => { _updMsg = m; Svc.Ui(Render); }); if (r.Length > 0) { _updMsg = r; Render(); } }, true),
                    Pill("Not this one", () => { S.UpdateSkipped = info.Version; S.Save(); Updater.Check(false).ContinueWith(_ => Svc.Ui(Render)); }).M(6), Pill("Release page", () => Svc.Open(info.Page)).M(6)), true);
            RowItem("How it works", "Perch downloads the release, checks it against its published checksum, closes, swaps the files, keeps your old version, and starts again. Nothing is replaced if the check fails. If Perch lives in a protected folder like Program Files, run it as administrator once, or update by hand.", null);
        }
        RowItem("Go back a version", "Puts the last version Perch kept back in place and restarts.", Pill("Roll back", () => { var r = Updater.Rollback(); if (r.Length > 0) { _updMsg = r; Render(); } }));
    }
    void DesktopPage()
    {
        Section("Taskbar");
        RowItem("Use Perch's glass taskbar", "Hides the Windows taskbar and shows a floating glass bar: Start, search, your launcher slots, open programs, quick settings and the clock. Click a program to switch to it, click the one in front to minimise it, right-click to close it. The Windows taskbar comes back when you turn this off or quit Perch.",
            new Toggle(S.GlassTaskbar, v => { S.GlassTaskbar = v; S.Save(); GlassTaskbar.Sync(); }));
        RowItem("Pinned programs", "The bar starts with the six launcher slots from the Tools tab.", null);
        Section("Live wallpaper");
        RowItem("Play a video or picture behind the desktop icons", "A looping, muted video (MP4, MOV, WMV, MKV, WebM) or any picture. It sits in the same layer as the Windows wallpaper, so icons stay on top.",
            new Toggle(S.LiveWallpaperOn, v => { S.LiveWallpaperOn = v; S.Save(); LiveWallpaper.Sync(); }));
        RowItem("File", S.LiveWallpaper.Length > 0 ? S.LiveWallpaper : "None chosen yet.", Pill("Choose…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Videos and pictures|*.mp4;*.mov;*.wmv;*.avi;*.mkv;*.webm;*.m4v;*.jpg;*.jpeg;*.png;*.bmp" };
            if (dlg.ShowDialog() == true) { S.LiveWallpaper = dlg.FileName; S.LiveWallpaperOn = true; S.Save(); LiveWallpaper.Sync(); Render(); }
        }));
        RowItem("Still wallpapers", "Pick one of your pictures or one that comes with Windows from the Manage tab, under Wallpaper.", null);
        Section("On the wallpaper");
        RowItem("Day clock", "Day, date and time in big spaced letters. Drag it where you want it; scroll on it to resize.", new Toggle(S.DayClock, v => { S.DayClock = v; S.Save(); DayClock.Sync(); }));
        foreach (var (id, label) in DeskWidget.Kinds)
        {
            var k = id;
            RowItem(label + " widget", "A glass tile on the desktop, behind every window. Drag it to move it.", new Toggle(S.DeskWidgets.Contains(k), v => { if (v) { if (!S.DeskWidgets.Contains(k)) S.DeskWidgets.Add(k); } else S.DeskWidgets.Remove(k); S.Save(); DeskWidget.Sync(); }));
        }
    }
}
