using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Perch;

public static class Svc
{
    public static readonly MediaService Media = new();
    public static readonly AudioEndpoint Speaker = new(0);
    public static readonly AudioEndpoint Mic = new(1);
    public static readonly Stats Stats = new();
    public static readonly Weather Weather = new();
    public static readonly Notices Notices = new();
    public static readonly FocusTimer Focus = new();
    public static readonly ClipHistory Clip = new();
    public static readonly List<string> Shelf = new();
    public static event Action? ShelfChanged;
    public static void AddToShelf(IEnumerable<string> files)
    {
        foreach (var f in files) if (!Shelf.Contains(f)) Shelf.Add(f);
        ShelfChanged?.Invoke();
    }
    public static void RaiseShelf() => ShelfChanged?.Invoke();
    public static void Ui(Action a) => Application.Current?.Dispatcher.BeginInvoke(a);
    public static void Open(string target, string args = "")
    {
        try { Process.Start(new ProcessStartInfo(target, args) { UseShellExecute = true }); }
        catch (Exception ex) { Notices.Add("Couldn't open that", ex.Message, ""); }
    }
}

// ───────────────────────── Notices ─────────────────────────
public record Notice(string Title, string Body, string Glyph, DateTime At);

public class Notices
{
    public readonly List<Notice> Items = new();
    public event Action<Notice>? Added;
    public event Action? Changed;
    public void Add(string title, string body, string glyph = "")
    {
        var n = new Notice(title, body, glyph, DateTime.Now);
        Items.Insert(0, n);
        while (Items.Count > 24) Items.RemoveAt(Items.Count - 1);
        Added?.Invoke(n); Changed?.Invoke();
    }
    public void Clear() { Items.Clear(); Changed?.Invoke(); }
    public void Remove(Notice n) { Items.Remove(n); Changed?.Invoke(); }
}

// Local-only endpoint so scripts can post a line to the island:
//   curl "http://127.0.0.1:47824/notify?title=Build&body=done"
public static class NotifyServer
{
    static HttpListener? _l;
    public static string Status = "Off";
    public static void Restart()
    {
        try { _l?.Close(); } catch { }
        _l = null; Status = "Off";
        var s = Settings.Current;
        if (!s.NotifyEndpoint) return;
        try
        {
            _l = new HttpListener();
            _l.Prefixes.Add($"http://127.0.0.1:{s.Port}/");
            _l.Start();
            Status = $"Listening on 127.0.0.1:{s.Port}";
            _ = Loop(_l);
        }
        catch (Exception ex) { Status = "Not running: " + ex.Message; _l = null; }
    }
    static async Task Loop(HttpListener l)
    {
        while (l.IsListening)
        {
            HttpListenerContext c;
            try { c = await l.GetContextAsync(); } catch { return; }
            try
            {
                // Browsers send an Origin header; refuse them so web pages can't post here.
                bool browser = c.Request.Headers["Origin"] != null || c.Request.Headers["Sec-Fetch-Site"] != null;
                if (browser || c.Request.Url?.AbsolutePath != "/notify") c.Response.StatusCode = browser ? 403 : 404;
                else
                {
                    var q = c.Request.QueryString;
                    string title = q["title"] ?? "Notice", body = q["body"] ?? "";
                    Svc.Ui(() => Svc.Notices.Add(title, body, ""));
                    c.Response.StatusCode = 204;
                }
                c.Response.Close();
            }
            catch { }
        }
    }
}

// ───────────────────────── Windows notification mirroring ─────────────────────────
// Reads the toasts other apps post, through Windows' notification listener, and repeats them on the island.
// Polled, because the listener's change event is only delivered to packaged apps.
public static class ToastMirror
{
    public static string Status = "Off";
    static readonly HashSet<uint> Seen = new();
    static bool _primed;
    static DispatcherTimer? _timer;

    public static async void Restart()
    {
        _timer?.Stop(); _timer = null; Seen.Clear(); _primed = false;
        if (!Settings.Current.MirrorNotifications) { Status = "Off"; return; }
        try
        {
            var l = UserNotificationListener.Current;
            var access = l.GetAccessStatus();
            if (access != UserNotificationListenerAccessStatus.Allowed) access = await l.RequestAccessAsync();
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                Status = "Windows hasn't allowed it. Turn on notification access for apps in Windows privacy settings.";
                return;
            }
            Status = "On";
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += async (_, _) => await Poll();
            _timer.Start();
            await Poll();
        }
        catch (Exception ex) { Status = "Not available here: " + ex.Message; }
    }
    static async Task Poll()
    {
        try
        {
            var list = await UserNotificationListener.Current.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var n in list)
            {
                if (!Seen.Add(n.Id) || !_primed) continue; // the first pass only records what was already there
                string app = "";
                try { app = n.AppInfo?.DisplayInfo?.DisplayName ?? ""; } catch { }
                var texts = new List<string>();
                try
                {
                    var b = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                    if (b != null) foreach (var t in b.GetTextElements()) if (!string.IsNullOrWhiteSpace(t.Text)) texts.Add(t.Text.Trim());
                }
                catch { }
                string title = texts.Count > 0 ? texts[0] : (app.Length > 0 ? app : "Notification");
                string body = string.Join(" ", texts.Skip(1));
                if (app.Length > 0 && app != title) body = body.Length > 0 ? $"{app}: {body}" : app;
                Svc.Notices.Add(title, body, "\uE8BD");
            }
            _primed = true;
        }
        catch (Exception ex) { Status = "Stopped: " + ex.Message; _timer?.Stop(); }
    }
}

// ───────────────────────── Monitor brightness (DDC/CI on the main screen) ─────────────────────────
public static class Brightness
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PHYSICAL_MONITOR { public IntPtr Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; }
    [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr mon, out uint count);
    [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr mon, uint count, [In, Out] PHYSICAL_MONITOR[] arr);
    [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint count, [In] PHYSICAL_MONITOR[] arr);
    [DllImport("dxva2.dll")] static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint cur, out uint max);
    [DllImport("dxva2.dll")] static extern bool SetMonitorBrightness(IntPtr h, uint value);

    static readonly object Gate = new(), Wire = new();
    static double? _pending; static bool _busy;

    static double? With(Func<IntPtr, double?> f)
    {
        try
        {
            lock (Wire)
            {
                var mon = MonitorFromWindow(GetDesktopWindow(), 1);
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(mon, out var n) || n == 0) return null;
                var arr = new PHYSICAL_MONITOR[n];
                if (!GetPhysicalMonitorsFromHMONITOR(mon, n, arr)) return null;
                try { return f(arr[0].Handle); } finally { DestroyPhysicalMonitors(n, arr); }
            }
        }
        catch { return null; }
    }
    // null when the screen doesn't answer (laptop panels and some monitors don't speak DDC/CI)
    public static Task<double?> Get() => Task.Run(() =>
        With(h => GetMonitorBrightness(h, out var mn, out var cur, out var mx) && mx > mn ? (cur - mn) / (double)(mx - mn) : (double?)null));
    // Monitors answer slowly, so only the newest value is sent while the slider moves.
    public static void Set(double v)
    {
        lock (Gate) { _pending = Math.Clamp(v, 0, 1); if (_busy) return; _busy = true; }
        Task.Run(() =>
        {
            while (true)
            {
                double p;
                lock (Gate) { if (_pending == null) { _busy = false; return; } p = _pending.Value; _pending = null; }
                With(h => { if (GetMonitorBrightness(h, out var mn, out _, out var mx)) SetMonitorBrightness(h, (uint)Math.Round(mn + p * (mx - mn))); return 0; });
            }
        });
    }
}

// ───────────────────────── Media (system media transport controls) ─────────────────────────
public class MediaService
{
    GlobalSystemMediaTransportControlsSessionManager? _mgr;
    GlobalSystemMediaTransportControlsSession? _s;
    public string Title = "", Artist = "", Source = "";
    public ImageSource? Art;
    public Color Tint = Color.FromRgb(0x1c, 0x1c, 0x1e);
    public bool Playing, HasSession;
    TimeSpan _pos, _dur; DateTimeOffset _posAt;
    public event Action? Changed;

    public TimeSpan Duration => _dur;
    public TimeSpan Position
    {
        get
        {
            var p = _pos;
            if (Playing) p += DateTimeOffset.Now - _posAt;
            return p < TimeSpan.Zero ? TimeSpan.Zero : (p > _dur && _dur > TimeSpan.Zero ? _dur : p);
        }
    }

    public async Task Init()
    {
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _mgr.CurrentSessionChanged += (_, _) => Svc.Ui(Hook);
            Hook();
        }
        catch { }
    }
    void Hook()
    {
        if (_mgr == null) return;
        if (_s != null)
        {
            try { _s.MediaPropertiesChanged -= OnProps; _s.PlaybackInfoChanged -= OnPlay; _s.TimelinePropertiesChanged -= OnTime; } catch { }
        }
        _s = _mgr.GetCurrentSession();
        HasSession = _s != null;
        if (_s != null)
        {
            _s.MediaPropertiesChanged += OnProps; _s.PlaybackInfoChanged += OnPlay; _s.TimelinePropertiesChanged += OnTime;
            Source = _s.SourceAppUserModelId ?? "";
        }
        else { Title = Artist = ""; Art = null; Playing = false; _pos = _dur = TimeSpan.Zero; }
        _ = Refresh(true);
    }
    void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => Svc.Ui(() => _ = Refresh(true));
    void OnPlay(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => Svc.Ui(() => _ = Refresh(false));
    void OnTime(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => Svc.Ui(() => _ = Refresh(false));

    async Task Refresh(bool props)
    {
        var s = _s;
        if (s == null) { Changed?.Invoke(); return; }
        try
        {
            Playing = s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var t = s.GetTimelineProperties();
            if (t != null) { _pos = t.Position; _dur = t.EndTime - t.StartTime; _posAt = t.LastUpdatedTime; }
            if (props)
            {
                var p = await s.TryGetMediaPropertiesAsync();
                Title = p?.Title ?? ""; Artist = p?.Artist ?? "";
                Art = null;
                if (p?.Thumbnail != null)
                {
                    using var ras = await p.Thumbnail.OpenReadAsync();
                    using var ms = new MemoryStream();
                    await ras.AsStreamForRead().CopyToAsync(ms);
                    ms.Position = 0;
                    var bmp = new BitmapImage();
                    bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = ms; bmp.DecodePixelWidth = 160; bmp.EndInit();
                    bmp.Freeze();
                    Art = bmp;
                    Tint = Average(bmp);
                }
            }
        }
        catch { }
        Changed?.Invoke();
    }
    static Color Average(BitmapSource src)
    {
        try
        {
            var f = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = f.PixelWidth, h = f.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            f.CopyPixels(px, stride, 0);
            long r = 0, g = 0, b = 0, n = 0;
            for (int i = 0; i < px.Length; i += 4 * 7) { b += px[i]; g += px[i + 1]; r += px[i + 2]; n++; }
            if (n == 0) return Color.FromRgb(0x1c, 0x1c, 0x1e);
            // keep it dark enough for white text
            double k = 0.55;
            return Color.FromRgb((byte)(r / n * k), (byte)(g / n * k), (byte)(b / n * k));
        }
        catch { return Color.FromRgb(0x1c, 0x1c, 0x1e); }
    }
    public async void Toggle() { try { if (_s != null) await _s.TryTogglePlayPauseAsync(); } catch { } }
    public async void Next() { try { if (_s != null) await _s.TrySkipNextAsync(); } catch { } }
    public async void Prev() { try { if (_s != null) await _s.TrySkipPreviousAsync(); } catch { } }
    public async void Seek(double fraction)
    {
        try
        {
            if (_s == null || _dur <= TimeSpan.Zero) return;
            var to = TimeSpan.FromSeconds(_dur.TotalSeconds * fraction);
            _pos = to; _posAt = DateTimeOffset.Now;
            await _s.TryChangePlaybackPositionAsync(to.Ticks);
        }
        catch { }
    }
}

// ───────────────────────── Core Audio (default endpoint volume / mute) ─────────────────────────
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out int state);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPropertyStore
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetAt(int i, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
}
[StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Id; public int Pid; }
[StructLayout(LayoutKind.Explicit)] struct PropVariant { [FieldOffset(0)] public short Vt; [FieldOffset(8)] public IntPtr Ptr; [FieldOffset(16)] public IntPtr Pad; }

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int GetChannelCount(out int count);
    [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
    [PreserveSig] int GetMasterVolumeLevel(out float db);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(int ch, float db, ref Guid ctx);
    [PreserveSig] int SetChannelVolumeLevelScalar(int ch, float level, ref Guid ctx);
    [PreserveSig] int GetChannelVolumeLevel(int ch, out float db);
    [PreserveSig] int GetChannelVolumeLevelScalar(int ch, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

public class AudioEndpoint
{
    readonly int _flow; // 0 render, 1 capture
    public AudioEndpoint(int flow) { _flow = flow; }

    IAudioEndpointVolume? Get(out IMMDevice? dev)
    {
        dev = null;
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (en.GetDefaultAudioEndpoint(_flow, _flow == 0 ? 1 : 2, out var d) != 0 || d == null) return null;
            dev = d;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (d.Activate(ref iid, 23, IntPtr.Zero, out var o) != 0) return null;
            return (IAudioEndpointVolume)o;
        }
        catch { return null; }
    }
    public bool Available => Get(out _) != null;
    // Level and mute from a single device lookup, for callers that poll.
    public bool Read(out double volume, out bool muted)
    {
        volume = 0; muted = false;
        var v = Get(out _); if (v == null) return false;
        v.GetMasterVolumeLevelScalar(out var f); v.GetMute(out muted); volume = f;
        return true;
    }
    public double Volume
    {
        get { var v = Get(out _); if (v == null) return 0; v.GetMasterVolumeLevelScalar(out var f); return f; }
        set { var v = Get(out _); var g = Guid.Empty; v?.SetMasterVolumeLevelScalar((float)Math.Clamp(value, 0, 1), ref g); }
    }
    public bool Muted
    {
        get { var v = Get(out _); if (v == null) return false; v.GetMute(out var m); return m; }
        set { var v = Get(out _); var g = Guid.Empty; v?.SetMute(value, ref g); }
    }
    public string Name
    {
        get
        {
            try
            {
                Get(out var d);
                if (d == null || d.OpenPropertyStore(0, out var ps) != 0) return "No device";
                var key = new PropertyKey { Id = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Pid = 14 }; // friendly name
                if (ps.GetValue(ref key, out var pv) != 0 || pv.Ptr == IntPtr.Zero) return "Default device";
                return Marshal.PtrToStringUni(pv.Ptr) ?? "Default device";
            }
            catch { return "Default device"; }
        }
    }
}

// ───────────────────────── System stats ─────────────────────────
public class Stats
{
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, a, b, c, d, e; }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    long _idle, _kernel, _user, _rx, _tx; DateTime _at = DateTime.MinValue;
    public double Cpu, Ram, DownBps, UpBps;

    public void Sample()
    {
        if (GetSystemTimes(out var i, out var k, out var u))
        {
            long di = i - _idle, dt = (k - _kernel) + (u - _user);
            if (_kernel != 0 && dt > 0) Cpu = Math.Clamp(1.0 - (double)di / dt, 0, 1);
            _idle = i; _kernel = k; _user = u;
        }
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref m)) Ram = m.dwMemoryLoad / 100.0;
        try
        {
            long rx = 0, tx = 0;
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var st = n.GetIPv4Statistics(); rx += st.BytesReceived; tx += st.BytesSent;
            }
            var now = DateTime.UtcNow; double secs = (now - _at).TotalSeconds;
            if (_at != DateTime.MinValue && secs > 0.2) { DownBps = Math.Max(0, (rx - _rx) / secs); UpBps = Math.Max(0, (tx - _tx) / secs); }
            _rx = rx; _tx = tx; _at = now;
        }
        catch { }
    }
    // GPU load from Windows' own "GPU Engine" counters (the 3D engines, summed over every process).
    public double Gpu = -1; // -1 while Windows offers no reading
    // One snapshot of the whole category per sample: far quicker than a counter object per engine.
    Dictionary<string, CounterSample> _gpuPrev = new();
    int _gpuBusy;
    public void SampleGpu()
    {
        if (System.Threading.Interlocked.Exchange(ref _gpuBusy, 1) == 1) return; // never queue up
        Task.Run(() =>
        {
            try
            {
                var data = new PerformanceCounterCategory("GPU Engine").ReadCategory()["Utilization Percentage"];
                var now = new Dictionary<string, CounterSample>();
                double sum = 0; bool compared = false;
                if (data != null)
                    foreach (InstanceData d in data.Values)
                    {
                        if (!d.InstanceName.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;
                        now[d.InstanceName] = d.Sample;
                        if (_gpuPrev.TryGetValue(d.InstanceName, out var old))
                        {
                            compared = true;
                            float v = CounterSample.Calculate(old, d.Sample);
                            if (!float.IsNaN(v) && !float.IsInfinity(v)) sum += v;
                        }
                    }
                _gpuPrev = now;
                // the first snapshot has nothing to compare against, so the reading starts with the second
                if (compared) Gpu = Math.Clamp(sum / 100.0, 0, 1);
                else if (now.Count == 0) Gpu = -1;
            }
            catch { Gpu = -1; }
            finally { System.Threading.Interlocked.Exchange(ref _gpuBusy, 0); }
        });
    }
    public static string Rate(double bps)
    {
        if (Settings.Current.SpeedUnit == "Mbps") { double mb = bps * 8 / 1e6; return mb >= 1 ? $"{mb:0.0} Mbps" : $"{mb * 1000:0} kbps"; }
        return bps >= 1 << 20 ? $"{bps / (1 << 20):0.0} MB/s" : bps >= 1024 ? $"{bps / 1024:0} KB/s" : $"{bps:0} B/s";
    }
}

// ───────────────────────── Weather (Open-Meteo, no key) ─────────────────────────
public class Weather
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    public bool Ready; public double Temp, High, Low; public int Code; public bool Day = true; public string Error = "";
    DateTime _at = DateTime.MinValue;
    public event Action? Changed;

    public string Condition => Code switch
    {
        0 => "Clear", 1 => "Mostly clear", 2 => "Partly cloudy", 3 => "Overcast", 45 or 48 => "Fog",
        >= 51 and <= 57 => "Drizzle", >= 61 and <= 67 => "Rain", >= 71 and <= 77 => "Snow",
        >= 80 and <= 82 => "Showers", >= 85 and <= 86 => "Snow showers", >= 95 => "Thunderstorm", _ => "—"
    };
    public string Glyph => Code switch { 0 or 1 => Day ? "" : "", _ => "" };

    public async Task<bool> SetCity(string name)
    {
        try
        {
            var json = await Http.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?count=1&name=" + Uri.EscapeDataString(name));
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0) return false;
            var r = res[0];
            var s = Settings.Current;
            s.City = r.GetProperty("name").GetString() ?? name;
            s.Lat = r.GetProperty("latitude").GetDouble(); s.Lon = r.GetProperty("longitude").GetDouble();
            s.Save();
            _at = DateTime.MinValue;
            await Refresh();
            return true;
        }
        catch { return false; }
    }
    public async Task Refresh(bool force = false)
    {
        var s = Settings.Current;
        if (string.IsNullOrEmpty(s.City)) { Ready = false; Changed?.Invoke(); return; }
        if (!force && DateTime.Now - _at < TimeSpan.FromMinutes(20)) return;
        _at = DateTime.Now;
        try
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={s.Lat.ToString(inv)}&longitude={s.Lon.ToString(inv)}" +
                      "&current=temperature_2m,weather_code,is_day&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1" +
                      (s.Fahrenheit ? "&temperature_unit=fahrenheit" : "");
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var cur = doc.RootElement.GetProperty("current");
            Temp = cur.GetProperty("temperature_2m").GetDouble();
            Code = cur.GetProperty("weather_code").GetInt32();
            Day = cur.GetProperty("is_day").GetInt32() == 1;
            var d = doc.RootElement.GetProperty("daily");
            High = d.GetProperty("temperature_2m_max")[0].GetDouble();
            Low = d.GetProperty("temperature_2m_min")[0].GetDouble();
            Ready = true; Error = "";
        }
        catch (Exception ex) { Error = ex.Message; }
        Changed?.Invoke();
    }
}

// ───────────────────────── Focus timer ─────────────────────────
public class FocusTimer
{
    public TimeSpan Remaining = TimeSpan.FromMinutes(Settings.Current.FocusMinutes);
    public bool Running;
    DateTime _last;
    public event Action? Changed;
    public void Toggle() { Running = !Running; _last = DateTime.Now; if (Remaining <= TimeSpan.Zero) Reset(); Changed?.Invoke(); }
    public void Reset() { Remaining = TimeSpan.FromMinutes(Settings.Current.FocusMinutes); }
    public void Adjust(int minutes)
    {
        var s = Settings.Current;
        s.FocusMinutes = Math.Clamp(s.FocusMinutes + minutes, 5, 120); s.Save();
        if (!Running) Reset(); else { var r = Remaining + TimeSpan.FromMinutes(minutes); Remaining = r > TimeSpan.Zero ? r : TimeSpan.FromSeconds(1); }
        Changed?.Invoke();
    }
    public void Tick()
    {
        if (!Running) return;
        var now = DateTime.Now; Remaining -= now - _last; _last = now;
        if (Remaining <= TimeSpan.Zero)
        {
            Running = false; Remaining = TimeSpan.Zero;
            Svc.Notices.Add("Focus session done", $"{Settings.Current.FocusMinutes} minutes. Take five.", "");
            System.Media.SystemSounds.Asterisk.Play();
            Reset();
        }
        Changed?.Invoke();
    }
}

// ───────────────────────── Clipboard history ─────────────────────────
public class ClipEntry
{
    public string? Text; public BitmapSource? Image; public DateTime At = DateTime.Now; public bool Pinned; public List<string>? Files;
    public bool IsImage => Image != null;
}

public class ClipHistory
{
    public readonly List<ClipEntry> Entries = new();
    public event Action? Changed;
    DateTime _ignoreUntil;
    static int MaxEntries => Math.Clamp(Settings.Current.ClipMaxEntries, 10, 500); const int MaxImages = 8;
    public ClipHistory() { Changed += SaveSoon; }

    public void Capture()
    {
        if (DateTime.Now < _ignoreUntil) return; // our own copy-back, not a new clip
        // apps the user listed (a password manager, say) never reach the history
        var src = Native.ForegroundExe(IntPtr.Zero);
        if (src.Length > 0 && Settings.Current.ClipIgnoredApps.Any(a => a.Equals(src, StringComparison.OrdinalIgnoreCase))) return;
        if (Tools2.SourceSaysPrivate()) return; // a password manager marked it private
        try
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                var t = System.Windows.Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(t)) return;
                if (Settings.Current.ClipSkipSensitive && Tools2.LooksSensitive(t)) return;
                Entries.RemoveAll(e => e.Text == t);
                Entries.Insert(0, new ClipEntry { Text = t });
            }
            else if (System.Windows.Clipboard.ContainsImage())
            {
                var img = System.Windows.Clipboard.GetImage();
                if (img == null) return;
                // re-encode to an opaque format so the copy doesn't depend on the source app staying open
                BitmapSource copy;
                if (img.Format.BitsPerPixel == 32)
                {
                    // copy the raw bytes and read them as opaque: a colour conversion here lifts the picture's brightness
                    int stride = img.PixelWidth * 4; var px = new byte[stride * img.PixelHeight];
                    img.CopyPixels(px, stride, 0);
                    copy = BitmapSource.Create(img.PixelWidth, img.PixelHeight, 96, 96, PixelFormats.Bgr32, null, px, stride);
                }
                else copy = new WriteableBitmap(new FormatConvertedBitmap(img, PixelFormats.Bgr32, null, 0));
                copy.Freeze();
                Entries.Insert(0, new ClipEntry { Image = copy });
                // pictures are large: keep only the newest few
                foreach (var old in Entries.Where(e => e.IsImage).Skip(MaxImages).ToList()) Entries.Remove(old);
            }
            else if (System.Windows.Clipboard.ContainsFileDropList())
            {
                var fl = System.Windows.Clipboard.GetFileDropList().Cast<string>().ToList();
                if (fl.Count == 0) return;
                string label = "Files: " + System.IO.Path.GetFileName(fl[0]) + (fl.Count > 1 ? $" +{fl.Count - 1} more" : "");
                Entries.RemoveAll(e => e.Files != null && e.Files.SequenceEqual(fl));
                Entries.Insert(0, new ClipEntry { Text = label, Files = fl });
            }
            else return;
            Trim(false);
            while (Entries.Count > MaxEntries) Entries.RemoveAt(Entries.Count - 1);
            Changed?.Invoke();
        }
        catch { } // clipboard can be locked by another app; the next update will catch up
    }
    // Clips older than the retention time go, except starred ones.
    public void Trim(bool notify = true)
    {
        int hours = Settings.Current.ClipKeepHours; if (hours <= 0) return;
        int gone = Entries.RemoveAll(e => (DateTime.Now - e.At).TotalHours > hours && !(e.Text != null && IsPinned(e.Text)));
        if (gone > 0 && notify) Changed?.Invoke();
    }
    // ── kept between sessions: encrypted for this Windows account, only when switched on ──
    class Saved { public string? T { get; set; } public List<string>? F { get; set; } public string? I { get; set; } public DateTime At { get; set; } }
    static string DataFile => System.IO.Path.Combine(Settings.Dir, "clip.dat");
    System.Windows.Threading.DispatcherTimer? _saveTimer;
    void SaveSoon()
    {
        if (!Settings.Current.ClipPersist) return;
        if (_saveTimer == null) { _saveTimer = new() { Interval = TimeSpan.FromSeconds(4) }; _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Save(); }; }
        _saveTimer.Stop(); _saveTimer.Start();
    }
    public void Save()
    {
        if (!Settings.Current.ClipPersist) return;
        var snap = Entries.ToList();
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var list = new List<Saved>();
                foreach (var e in snap)
                {
                    string? png = null;
                    if (e.Image != null) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(e.Image)); using var ms = new System.IO.MemoryStream(); enc.Save(ms); png = Convert.ToBase64String(ms.ToArray()); }
                    list.Add(new Saved { T = e.Text, F = e.Files, I = png, At = e.At });
                }
                System.IO.Directory.CreateDirectory(Settings.Dir);
                System.IO.File.WriteAllText(DataFile, Secret.Seal(System.Text.Json.JsonSerializer.Serialize(list)));
            }
            catch { }
        });
    }
    public void Load()
    {
        try
        {
            if (!Settings.Current.ClipPersist || !System.IO.File.Exists(DataFile)) return;
            var json = Secret.Open(System.IO.File.ReadAllText(DataFile)); if (json.Length == 0) return;
            var list = System.Text.Json.JsonSerializer.Deserialize<List<Saved>>(json); if (list == null) return;
            foreach (var s in list)
            {
                var e = new ClipEntry { Text = s.T, Files = s.F, At = s.At };
                if (s.I != null)
                {
                    using var ms = new System.IO.MemoryStream(Convert.FromBase64String(s.I));
                    var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad); var f = dec.Frames[0]; f.Freeze(); e.Image = f;
                }
                Entries.Add(e);
            }
            Trim(false); Changed?.Invoke();
        }
        catch { }
    }
    public static void Forget() { try { System.IO.File.Delete(DataFile); } catch { } }

    // Everything that is not starred goes; starred clips are kept.
    public void ClearUnpinned()
    {
        Entries.RemoveAll(e => e.IsImage || !IsPinned(e.Text!));
        Changed?.Invoke();
    }
    public void CopyBack(ClipEntry e)
    {
        _ignoreUntil = DateTime.Now.AddMilliseconds(600);
        try
        {
            if (e.Image != null) System.Windows.Clipboard.SetImage(e.Image);
            else if (e.Files != null) { var sc = new System.Collections.Specialized.StringCollection(); sc.AddRange(e.Files.ToArray()); System.Windows.Clipboard.SetFileDropList(sc); }
            else if (e.Text != null) System.Windows.Clipboard.SetDataObject(e.Text, true);
        }
        catch { }
    }
    public bool IsPinned(string t) => Settings.Current.Pinned.Contains(t);
    public void TogglePin(string t)
    {
        var p = Settings.Current.Pinned;
        if (!p.Remove(t)) p.Insert(0, t);
        Settings.Current.Save(); Changed?.Invoke();
    }
    public void Remove(ClipEntry e)
    {
        Entries.Remove(e);
        if (e.Text != null) { Entries.RemoveAll(x => x.Text == e.Text); Settings.Current.Pinned.Remove(e.Text); Settings.Current.Save(); }
        Changed?.Invoke();
    }
}

// ───────────────────────── Native bits ─────────────────────────
public static class Native
{
    // Average brightness (0..1) of a patch of the screen, in physical pixels.
    public static double ScreenLuma(int x, int y, int w, int h) => ScreenSample(x, y, w, h, out _);
    // Same patch, also giving its average colour: one capture serves both readings.
    public static double ScreenSample(int x, int y, int w, int h, out System.Windows.Media.Color average)
    {
        average = default;
        try
        {
            if (w < 1 || h < 1) return -1;
            using var shot = new System.Drawing.Bitmap(w, h);
            using (var g = System.Drawing.Graphics.FromImage(shot)) g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
            using var small = new System.Drawing.Bitmap(8, 4);
            using (var g = System.Drawing.Graphics.FromImage(small))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.DrawImage(shot, 0, 0, 8, 4);
            }
            double sum = 0, r = 0, g2 = 0, b = 0;
            for (int i = 0; i < 8; i++) for (int j = 0; j < 4; j++) { var c = small.GetPixel(i, j); sum += (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0; r += c.R; g2 += c.G; b += c.B; }
            average = System.Windows.Media.Color.FromRgb((byte)(r / 32), (byte)(g2 / 32), (byte)(b / 32));
            return sum / 32;
        }
        catch { return -1; }
    }
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int idx);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int idx, int val);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);

    // true when the app in front covers the whole main screen (a game, a video, a presentation)
    // A maximised, ordinary window on the main screen is in front: a slim island keeps clear of its tabs.
    public static bool ForegroundIsMaximized(IntPtr self)
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero || h == self) return false;
            var sb = new System.Text.StringBuilder(64);
            GetClassName(h, sb, 64);
            if (sb.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "XamlExplorerHostIslandWindow") return false;
            if ((GetWindowLong(h, -20) & (0x20 | 0x08000000 | 0x80)) != 0) return false;
            if (!IsZoomed(h) || !GetWindowRect(h, out var r)) return false;
            var b = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
            return cx >= b.Left && cx < b.Right && cy >= b.Top && cy < b.Bottom;
        }
        catch { return false; }
    }
    // Keeps a window out of screenshots, recordings and screen sharing (Windows 10 2004 and later).
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    public static void ExcludeFromCapture(IntPtr hwnd, bool on) { if (hwnd != IntPtr.Zero) try { SetWindowDisplayAffinity(hwnd, on ? 0x11u : 0u); } catch { } }

    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    static uint _exePid; static string _exeName = "";
    // The program file name (like "game.exe") of the window in front; empty for Perch itself or when it cannot be read.
    public static string ForegroundExe(IntPtr self)
    {
        try
        {
            var h = GetForegroundWindow(); if (h == IntPtr.Zero || h == self) return "";
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == (uint)Environment.ProcessId) return "";
            if (pid != _exePid) { using var p = System.Diagnostics.Process.GetProcessById((int)pid); _exeName = p.ProcessName + ".exe"; _exePid = pid; }
            return _exeName;
        }
        catch { return ""; }
    }
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    public static IntPtr RootWindowAt(int x, int y) { try { var h = WindowFromPoint(new POINT { X = x, Y = y }); return h == IntPtr.Zero ? IntPtr.Zero : GetAncestor(h, 2); } catch { return IntPtr.Zero; } }
    // Brings an ordinary app window forward; never the desktop, the taskbar, menus or tool windows.
    public static void RaiseIfOrdinary(IntPtr h)
    {
        try
        {
            if (h == GetForegroundWindow() || !IsWindowVisible(h)) return;
            var sb = new System.Text.StringBuilder(64); GetClassName(h, sb, 64); string cls = sb.ToString();
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "#32768" or "XamlExplorerHostIslandWindow" or "TopLevelWindowForOverflowXamlIsland") return;
            if ((GetWindowLong(h, -20) & (0x80 | 0x08000000 | 0x20)) != 0) return;
            GetWindowThreadProcessId(h, out uint pid); if (pid == (uint)Environment.ProcessId) return;
            keybd_event(0x12, 0, 0, UIntPtr.Zero); SetForegroundWindow(h); keybd_event(0x12, 0, 2, UIntPtr.Zero); // a tap of Alt lets Windows allow the switch
        }
        catch { }
    }
    // Sends Ctrl+V to whatever has the keyboard, after letting go of the hotkey's own modifiers.
    public static void SendPaste()
    {
        foreach (byte k in new byte[] { 0x12, 0x10, 0x5B, 0x5C }) keybd_event(k, 0, 2, UIntPtr.Zero);
        keybd_event(0x11, 0, 0, UIntPtr.Zero); keybd_event(0x56, 0, 0, UIntPtr.Zero); keybd_event(0x56, 0, 2, UIntPtr.Zero); keybd_event(0x11, 0, 2, UIntPtr.Zero);
    }
    public static bool ForegroundIsFullscreen(IntPtr self)
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero || h == self) return false;
            var sb = new System.Text.StringBuilder(64);
            GetClassName(h, sb, 64);
            var cls = sb.ToString();
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "XamlExplorerHostIslandWindow") return false;
            // click-through, no-activate and tool windows are overlays, not apps
            if ((GetWindowLong(h, -20) & (0x20 | 0x08000000 | 0x80)) != 0) return false;
            // a maximised or titled window is ordinary work, even when the taskbar auto-hides and it reaches every edge
            if (IsZoomed(h) || (GetWindowLong(h, -16) & 0x00C00000) == 0x00C00000) return false;
            if (!GetWindowRect(h, out var r)) return false;
            var b = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
        }
        catch { return false; }
    }

    public static ImageSource? FileIcon(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (ico == null) return null;
            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
    }

    public static string? Screenshot()
    {
        try
        {
            var b = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            using var bmp = new System.Drawing.Bitmap(b.Width, b.Height);
            using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(b.Left, b.Top, 0, 0, b.Size);
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Perch");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"Screenshot {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
            bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            return file;
        }
        catch { return null; }
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static void SetStartup(bool on)
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (on) k?.SetValue("Perch", $"\"{Environment.ProcessPath}\"");
            else k?.DeleteValue("Perch", false);
        }
        catch { }
    }

    // Windows power plans: power saver, balanced, high performance
    public static readonly string[] Plans = { "a1841308-3541-4fab-bc81-f71556f20b4a", "381b4222-f694-41f0-9685-ff5bb260df2e", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" };
    static string Run(string exe, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
            var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(3000); return o;
        }
        catch { return ""; }
    }
    public static int ActivePlan()
    {
        var o = Run("powercfg", "/getactivescheme").ToLowerInvariant();
        return Array.FindIndex(Plans, p => o.Contains(p));
    }
    public static bool SetPlan(int i) { Run("powercfg", "/setactive " + Plans[i]); return ActivePlan() == i; }
}
