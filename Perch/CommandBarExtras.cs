using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace Perch;

public partial class Settings
{
    public Dictionary<string, int> CmdUsage { get; set; } = new();     // how often each command bar answer was chosen, so the usual ones come first
}

// More things the command bar can find and do: open windows, programs to quit, Windows settings pages, power, files, emoji and colours.
public static class CommandBarExtras
{
    public sealed record X(string Glyph, string Title, string Sub, Action Run);
    static readonly (string Name, string Uri, string Words)[] Pages =
    {
        ("Wi-Fi settings", "ms-settings:network-wifi", "wifi wireless internet"), ("Bluetooth settings", "ms-settings:bluetooth", "devices pair"), ("Display settings", "ms-settings:display", "screen resolution brightness scale monitor"),
        ("Sound settings", "ms-settings:sound", "volume audio output input speakers"), ("Installed apps", "ms-settings:appsfeatures", "uninstall programs apps features"), ("Default apps", "ms-settings:defaultapps", "browser open with"),
        ("Notification settings", "ms-settings:notifications", "alerts banners"), ("Battery settings", "ms-settings:batterysaver", "power saver usage"), ("Storage settings", "ms-settings:storagesense", "disk space cleanup"),
        ("Personalisation", "ms-settings:personalization", "theme wallpaper colours background"), ("Network settings", "ms-settings:network", "ethernet internet proxy"), ("VPN settings", "ms-settings:network-vpn", "vpn"),
        ("Privacy settings", "ms-settings:privacy", "permissions camera microphone location"), ("Windows Update", "ms-settings:windowsupdate", "updates upgrade"), ("Account settings", "ms-settings:yourinfo", "profile sign in"),
        ("Date and time", "ms-settings:dateandtime", "clock time zone"), ("Language and region", "ms-settings:regionlanguage", "keyboard region format"), ("Keyboard settings", "ms-settings:typing", "typing autocorrect"),
        ("Mouse settings", "ms-settings:mousetouchpad", "pointer scroll buttons"), ("Touchpad settings", "ms-settings:devices-touchpad", "gestures"), ("Printers and scanners", "ms-settings:printers", "printer scanner"),
        ("Startup apps", "ms-settings:startupapps", "login boot"), ("Night light", "ms-settings:nightlight", "warm colour blue light"), ("Focus", "ms-settings:quietmomentshome", "do not disturb"),
        ("Taskbar settings", "ms-settings:taskbar", "start menu icons"), ("Multitasking", "ms-settings:multitasking", "snap windows desktops"), ("About this PC", "ms-settings:about", "specs version device name"),
        ("Camera privacy", "ms-settings:privacy-webcam", "webcam"), ("Microphone privacy", "ms-settings:privacy-microphone", "mic"), ("Accessibility", "ms-settings:easeofaccess", "text size contrast narrator"),
    };
    static readonly (string Emoji, string Words)[] Emoji =
    {
        ("😀", "smile grin happy"), ("😂", "laugh joy tears lol"), ("🙂", "slight smile"), ("😉", "wink"), ("😍", "love heart eyes"), ("😘", "kiss"), ("😎", "cool sunglasses"), ("🤔", "think hmm"), ("😅", "sweat nervous"), ("😢", "cry sad"),
        ("😭", "sob"), ("😡", "angry mad"), ("🥳", "party celebrate"), ("😴", "sleep tired"), ("🤯", "mind blown"), ("🙄", "eye roll"), ("😬", "grimace"), ("🥲", "smile tear"), ("👍", "thumbs up yes ok"), ("👎", "thumbs down no"),
        ("👏", "clap applause"), ("🙏", "pray thanks please"), ("💪", "strong muscle"), ("👀", "eyes look"), ("🔥", "fire hot"), ("✨", "sparkles"), ("🎉", "tada party"), ("❤️", "heart red love"), ("💔", "broken heart"), ("💯", "hundred perfect"),
        ("✅", "check done yes"), ("❌", "cross no wrong"), ("⚠️", "warning"), ("❓", "question"), ("❗", "exclamation"), ("⭐", "star"), ("🌟", "glowing star"), ("☀️", "sun"), ("🌙", "moon night"), ("⛅", "cloud sun"),
        ("🌧️", "rain"), ("❄️", "snow cold"), ("🌈", "rainbow"), ("🍕", "pizza"), ("🍔", "burger"), ("🍟", "fries"), ("☕", "coffee"), ("🍺", "beer"), ("🍷", "wine"), ("🎂", "cake birthday"),
        ("🐶", "dog"), ("🐱", "cat"), ("🦊", "fox"), ("🐼", "panda"), ("🦄", "unicorn"), ("🚀", "rocket launch"), ("💡", "idea light bulb"), ("📌", "pin"), ("📎", "paperclip"), ("📝", "note memo"),
        ("📅", "calendar date"), ("📧", "email mail"), ("📞", "phone call"), ("💻", "laptop computer"), ("🖥️", "desktop"), ("⌨️", "keyboard"), ("🖱️", "mouse"), ("🔒", "lock secure"), ("🔑", "key"), ("🔔", "bell notification"),
        ("🎵", "music note"), ("🎧", "headphones"), ("📷", "camera photo"), ("🎮", "game controller"), ("🏆", "trophy win"), ("⏰", "alarm clock"), ("⌛", "hourglass wait"), ("💰", "money bag"), ("🧠", "brain"), ("👋", "wave hello bye"),
        ("🤝", "handshake deal"), ("🙌", "raised hands"), ("🤷", "shrug"), ("🤦", "facepalm"), ("😇", "angel"), ("🤩", "star struck"), ("😋", "yum"), ("🥰", "love smile"), ("🫡", "salute"), ("🫶", "heart hands"),
        ("→", "arrow right"), ("←", "arrow left"), ("↑", "arrow up"), ("↓", "arrow down"), ("©", "copyright"), ("™", "trademark"), ("€", "euro"), ("£", "pound"), ("°", "degree"), ("•", "bullet"),
    };
    static readonly string[] Dirs = { Environment.SpecialFolder.Desktop.ToString(), Environment.SpecialFolder.MyDocuments.ToString(), Environment.SpecialFolder.MyPictures.ToString() };

    public static List<X> Get(string q)
    {
        var res = new List<X>(); string lower = q.ToLowerInvariant().Trim(); if (lower.Length == 0) return res;
        try
        {
            // open windows, and a quick way to quit them
            bool wantQuit = lower.StartsWith("quit "); string name = wantQuit ? lower[5..].Trim() : lower;
            if (name.Length >= 2)
                foreach (var w in WinList.List(true).Where(w => (w.Title + " " + w.Exe).Contains(name, StringComparison.OrdinalIgnoreCase)).Take(wantQuit ? 4 : 3))
                {
                    var win = w;
                    if (wantQuit) res.Add(new X("", "Quit " + System.IO.Path.GetFileNameWithoutExtension(win.Exe), "Asks every window of it to close, so it can ask you to save.", () => { foreach (var o in WinList.List(true).Where(o => o.Pid == win.Pid)) WinList.Close(o.Handle); }));
                    else res.Add(new X("", win.Title.Length > 60 ? win.Title[..59] + "…" : win.Title, "Window · " + win.Exe + " · Enter switches to it", () => WinList.Activate(win.Handle)));
                }
            // Windows settings pages
            if (lower.Length >= 3) foreach (var p in Pages.Where(p => p.Name.Contains(lower, StringComparison.OrdinalIgnoreCase) || p.Words.Contains(lower, StringComparison.OrdinalIgnoreCase)).Take(3)) { var pg = p; res.Add(new X("", pg.Name, "Opens Windows Settings", () => Svc.Open(pg.Uri))); }
            // power
            void Power(string title, string verb, Action go, string sub) { if (title.StartsWith(lower) || verb.StartsWith(lower)) res.Add(new X("", title, sub, () => { if (MessageBox.Show(title + "?", "Perch", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) go(); })); }
            if (lower.Length >= 3)
            {
                Power("Sleep", "sleep", () => System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false), "Puts the PC to sleep.");
                Power("Restart", "reboot", () => Process.Start(new ProcessStartInfo("shutdown", "/r /t 3") { CreateNoWindow = true, UseShellExecute = false }), "Restarts the PC.");
                Power("Shut down", "power off", () => Process.Start(new ProcessStartInfo("shutdown", "/s /t 3") { CreateNoWindow = true, UseShellExecute = false }), "Turns the PC off.");
                Power("Log out", "sign out", () => Process.Start(new ProcessStartInfo("shutdown", "/l") { CreateNoWindow = true, UseShellExecute = false }), "Signs you out.");
            }
            // switches
            void Sw(string title, string keys, Action go, string sub = "") { if (keys.Contains(lower)) res.Add(new X("", title, sub, go)); }
            if (lower.Length >= 3)
            {
                Sw("Switch between light and dark", "dark mode light theme appearance", Tools.ToggleDarkMode); Sw("Show or hide desktop icons", "desktop icons hide", Tools.ToggleDesktopIcons);
                Sw("Show or hide hidden files", "hidden files show", Tools2.ToggleHidden); Sw("Show or hide file extensions", "file extensions show", Tools2.ToggleExtensions);
                Sw("Empty the Recycle Bin", "empty trash recycle bin", Tools.EmptyBin); Sw("Pick a colour from the screen", "colour color picker eyedropper", Tools.PickColour); Sw("Copy text from the screen", "ocr text scan qr", Tools.GrabText);
                Sw("Take a screenshot", "screenshot capture snip", () => Shot.Start("area")); Sw("Record the screen", "record screen video", Rec.Toggle); Sw("Open the camera mirror", "camera mirror webcam", CameraWindow.Toggle);
                Sw("Start cleaning mode", "cleaning keyboard lock wipe", CleaningMode.Start); Sw("Open the snippet menu", "snippets text expand", QuickPicks.Snippets); Sw("Quit every other program", "quit all others close apps", () => Tools2.QuitOthers(false));
                Sw("Show hidden files", "hidden", Tools2.ToggleHidden);
            }
            // a colour typed as hex
            if (System.Text.RegularExpressions.Regex.IsMatch(lower, "^#?[0-9a-f]{6}$"))
            {
                string h = lower.TrimStart('#'); int r = Convert.ToInt32(h[..2], 16), g = Convert.ToInt32(h[2..4], 16), b = Convert.ToInt32(h[4..], 16);
                foreach (var fmt in new[] { "hex", "rgb", "hsl" }) { var old = Settings.Current.ColourFormat; Settings.Current.ColourFormat = fmt; var text = Tools2.Colour(r, g, b); Settings.Current.ColourFormat = old; res.Add(new X("", text, "Colour · Enter copies it", () => { try { Clipboard.SetText(text); } catch { } })); }
            }
            // emoji
            if (lower.Length >= 3) foreach (var e in Emoji.Where(e => e.Words.Contains(lower)).Take(5)) { var em = e.Emoji; res.Add(new X("", em + "   " + e.Words.Split(' ')[0], "Emoji · Enter copies it", () => { try { Clipboard.SetText(em); } catch { } })); }
            // dates
            if (lower is "today" or "date" or "now" or "time") res.Add(new X("", DateTime.Now.ToString("dddd d MMMM yyyy, HH:mm"), "Enter copies it", () => { try { Clipboard.SetText(DateTime.Now.ToString("yyyy-MM-dd HH:mm")); } catch { } }));
            // files in the usual folders, by name
            if (lower.Length >= 3 && !wantQuit)
            {
                var sw = Stopwatch.StartNew(); int found = 0;
                foreach (var sf in new[] { Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyPictures })
                {
                    var dir = Environment.GetFolderPath(sf); if (!Directory.Exists(dir)) continue;
                    try
                    {
                        foreach (var f in Directory.EnumerateFileSystemEntries(dir, "*" + lower + "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true }))
                        {
                            var path = f; bool isDir = Directory.Exists(path); res.Add(new X(isDir ? "" : "", System.IO.Path.GetFileName(path), (isDir ? "Folder · " : "File · ") + path, () => Svc.Open(path)));
                            if (++found >= 4 || sw.ElapsedMilliseconds > 180) break;
                        }
                    }
                    catch { }
                    if (found >= 4 || sw.ElapsedMilliseconds > 180) break;
                }
            }
        }
        catch { }
        // what you pick most comes first
        var usage = Settings.Current.CmdUsage;
        return res.OrderByDescending(x => usage.GetValueOrDefault(x.Title, 0)).ToList();
    }
    public static void Used(string title) { var u = Settings.Current.CmdUsage; u[title] = u.GetValueOrDefault(title, 0) + 1; if (u.Count > 400) foreach (var k in u.OrderBy(k => k.Value).Take(100).Select(k => k.Key).ToList()) u.Remove(k); Settings.Current.Save(); }
}
