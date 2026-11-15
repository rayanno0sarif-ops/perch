using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using static Perch.UI;

namespace Perch;

public partial class Settings
{
    public Dictionary<string, int> DisplayDim { get; set; } = new();        // per display: how much of the picture's light is left, in percent
    public int DisplayStep { get; set; } = 10;                              // what one press of the brightness shortcuts changes
    public bool AwakeExternal { get; set; }                                 // stay awake while a second display is connected
    public bool AwakeJiggle { get; set; }                                   // nudge the pointer now and then so chat apps do not show you as away
    public int AwakeJiggleSec { get; set; } = 60;
    public bool AwakePauseLocked { get; set; } = true;                      // no rules apply while the screen is locked
    public bool AlertCpu { get; set; }
    public int AlertCpuPct { get; set; } = 90;
    public bool AlertMem { get; set; }
    public int AlertMemPct { get; set; } = 90;
    public bool AlertDisk { get; set; }
    public int AlertDiskPct { get; set; } = 10;                             // free space, in percent
    public int AlertCooldownMin { get; set; } = 30;
}

// Brightness and speaker volume of external monitors over DDC/CI, and a dimming layer for the ones that cannot (or to go below their minimum).
public static class Displays
{
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorProc cb, IntPtr data);
    delegate bool MonitorProc(IntPtr mon, IntPtr hdc, ref RECT r, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFOEX { public int Size; public RECT Mon, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFOEX mi);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct PHYS { public IntPtr Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Desc; }
    [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr mon, out uint n);
    [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr mon, uint n, [Out] PHYS[] arr);
    [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint n, PHYS[] arr);
    [DllImport("dxva2.dll")] static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint cur, out uint max);
    [DllImport("dxva2.dll")] static extern bool SetMonitorBrightness(IntPtr h, uint v);
    [DllImport("dxva2.dll")] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, IntPtr type, out uint cur, out uint max);
    [DllImport("dxva2.dll")] static extern bool SetVCPFeature(IntPtr h, byte code, uint v);

    public class Mon { public string Name = "", Device = ""; public IntPtr HMon; public bool Primary; public bool Ddc; public int Min, Cur, Max = 100; public bool Speakers; public int Volume = -1; }

    public static List<Mon> List()
    {
        var res = new List<Mon>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hm, IntPtr dc, ref RECT r, IntPtr d) =>
        {
            var mi = new MONITORINFOEX { Size = Marshal.SizeOf<MONITORINFOEX>() }; GetMonitorInfo(hm, ref mi);
            var m = new Mon { HMon = hm, Device = mi.Device, Primary = (mi.Flags & 1) != 0, Name = "Display " + (res.Count + 1) };
            try
            {
                if (GetNumberOfPhysicalMonitorsFromHMONITOR(hm, out uint n) && n > 0)
                {
                    var arr = new PHYS[n]; if (GetPhysicalMonitorsFromHMONITOR(hm, n, arr))
                    {
                        if (!string.IsNullOrWhiteSpace(arr[0].Desc) && !arr[0].Desc.Contains("Generic PnP", StringComparison.OrdinalIgnoreCase)) m.Name = arr[0].Desc;
                        if (GetMonitorBrightness(arr[0].Handle, out uint mn, out uint cu, out uint mx) && mx > mn) { m.Ddc = true; m.Min = (int)mn; m.Cur = (int)cu; m.Max = (int)mx; }
                        if (GetVCPFeatureAndVCPFeatureReply(arr[0].Handle, 0x62, IntPtr.Zero, out uint vol, out uint vmax) && vmax > 0) { m.Speakers = true; m.Volume = (int)(vol * 100 / vmax); }
                        DestroyPhysicalMonitors(n, arr);
                    }
                }
            }
            catch { }
            res.Add(m); return true;
        }, IntPtr.Zero);
        return res;
    }
    static T WithPhys<T>(Mon m, Func<IntPtr, T> f, T fallback)
    {
        try
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(m.HMon, out uint n) || n == 0) return fallback;
            var arr = new PHYS[n]; if (!GetPhysicalMonitorsFromHMONITOR(m.HMon, n, arr)) return fallback;
            try { return f(arr[0].Handle); } finally { DestroyPhysicalMonitors(n, arr); }
        }
        catch { return fallback; }
    }
    public static bool SetBrightness(Mon m, int percent)
    {
        int v = m.Min + (m.Max - m.Min) * Math.Clamp(percent, 0, 100) / 100;
        return WithPhys(m, h => SetMonitorBrightness(h, (uint)v), false);
    }
    public static bool SetVolume(Mon m, int percent) => WithPhys(m, h => SetVCPFeature(h, 0x62, (uint)Math.Clamp(percent, 0, 100)), false);
    public static bool SetMute(Mon m, bool mute) => WithPhys(m, h => SetVCPFeature(h, 0x8D, mute ? 1u : 2u), false);

    // ── the dimming layer ──
    static readonly Dictionary<string, Window> _layers = new();
    public static void Dim(string device, int percentLeft)
    {
        percentLeft = Math.Clamp(percentLeft, 10, 100); Settings.Current.DisplayDim[device] = percentLeft;
        if (percentLeft >= 100) { if (_layers.Remove(device, out var w0)) w0.Close(); return; }
        var scr = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == device) ?? System.Windows.Forms.Screen.PrimaryScreen!;
        if (!_layers.TryGetValue(device, out var w))
        {
            w = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Black, ShowInTaskbar = false, Topmost = true, ShowActivated = false, ResizeMode = ResizeMode.NoResize, Title = "Perch dim", IsHitTestVisible = false };
            w.SourceInitialized += (_, _) =>
            {
                var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x20 | 0x80 | 0x08000000); // click-through, no taskbar, no focus
                SetWindowPos(h, IntPtr.Zero, scr.Bounds.Left, scr.Bounds.Top, scr.Bounds.Width, scr.Bounds.Height, 0x0010 | 0x0004);
            };
            _layers[device] = w; w.Left = scr.Bounds.Left; w.Top = scr.Bounds.Top; w.Width = 100; w.Height = 100; w.Show();
        }
        w.Opacity = 1 - percentLeft / 100.0;
    }
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    public static void Restore() { foreach (var kv in Settings.Current.DisplayDim.ToList()) if (kv.Value < 100) Dim(kv.Key, kv.Value); }

    // A shortcut: change the brightness of the display the pointer is on.
    public static void Nudge(int dir)
    {
        var scr = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position); var s = Settings.Current;
        var mon = List().FirstOrDefault(m => m.Device == scr.DeviceName); if (mon == null) return;
        int step = Math.Clamp(s.DisplayStep, 2, 25) * dir;
        if (mon.Ddc && !(dir < 0 && mon.Cur <= mon.Min)) { int now = (mon.Cur - mon.Min) * 100 / Math.Max(1, mon.Max - mon.Min); int to = Math.Clamp(now + step, 0, 100); SetBrightness(mon, to); Svc.Ui(() => IslandWindow.Instance.Flash("\uE706", $"Brightness {to}%")); return; }
        int cur = s.DisplayDim.TryGetValue(mon.Device, out var d) ? d : 100; int next = Math.Clamp(cur + step, 10, 100); Dim(mon.Device, next); s.Save();
        Svc.Ui(() => IslandWindow.Instance.Flash("\uE706", $"Dimmed to {next}%"));
    }
}

// Warnings that the PC is under strain, each at most once in the cooldown.
public static class MonitorAlerts
{
    static System.Diagnostics.PerformanceCounter? _cpu; static int _hot; static readonly Dictionary<string, DateTime> _last = new(); static int _n;
    [StructLayout(LayoutKind.Sequential)] struct MEMSTAT { public uint Length, Load; public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirt, AvailVirt, AvailExt; }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMSTAT m);
    static bool Cool(string key) { if (_last.TryGetValue(key, out var t) && (DateTime.Now - t).TotalMinutes < Math.Max(1, Settings.Current.AlertCooldownMin)) return false; _last[key] = DateTime.Now; return true; }
    public static void Tick(Settings s)
    {
        if (!(s.AlertCpu || s.AlertMem || s.AlertDisk)) return;
        _n++;
        try
        {
            if (s.AlertCpu)
            {
                _cpu ??= new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total"); float v = _cpu.NextValue();
                _hot = v >= s.AlertCpuPct ? _hot + 1 : 0;
                if (_hot >= 60 && Cool("cpu")) Svc.Ui(() => Svc.Notices.Add("The processor has been busy", $"{v:0}% for a minute. Open Manage, then Processes, to see what is using it.", "\uE950"));
            }
            if (_n % 30 != 0) return;
            if (s.AlertMem) { var m = new MEMSTAT { Length = (uint)Marshal.SizeOf<MEMSTAT>() }; if (GlobalMemoryStatusEx(ref m) && m.Load >= s.AlertMemPct && Cool("mem")) Svc.Ui(() => Svc.Notices.Add("Memory is nearly full", $"{m.Load}% in use. Open Manage, then Processes.", "\uE950")); }
            if (s.AlertDisk) { var d = new System.IO.DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\"); double free = d.AvailableFreeSpace * 100.0 / d.TotalSize; if (free <= s.AlertDiskPct && Cool("disk")) Svc.Ui(() => Svc.Notices.Add("The disk is nearly full", $"{free:0}% free on {d.Name}. Open Manage, then Cleaner.", "\uE950")); }
        }
        catch { }
    }
}
