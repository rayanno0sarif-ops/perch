using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static Perch.UI;

namespace Perch;

// ───────────────────────── Mouse and keyboard tweaks ─────────────────────────
// One background thread owns the low-level mouse and keyboard hooks. Every callback does a few comparisons and returns:
// nothing here waits, blocks or reads the disk, because Windows drops a hook that answers too slowly.
// Each tweak is off until it is switched on, and a hook is only installed when some tweak needs it.
public static partial class Hooks
{
    // ── native ──
    delegate IntPtr Proc(int code, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, Proc fn, IntPtr mod, uint tid);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr h, uint a, uint b);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint tid, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern UIntPtr SetTimer(IntPtr h, UIntPtr id, uint ms, IntPtr fn);
    [DllImport("user32.dll")] static extern bool KillTimer(IntPtr h, UIntPtr id);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder buf, int cch, uint flags, IntPtr layout);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
    [StructLayout(LayoutKind.Sequential)] struct KBD { public uint vk, scan, flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct MSL { public int x, y; public uint data, flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public int mouseData; public uint dwFlags, time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public INPUTUNION u; }

    // events Perch sends itself carry this mark, so they are never treated as the user's own input
    static readonly UIntPtr Magic = (UIntPtr)0x50455243;
    static INPUT Key(ushort vk, bool up)
    {
        bool ext = vk is >= 0x21 and <= 0x28 or 0x2D or 0x2E or 0x5B or 0x5C;
        return new INPUT { type = 1, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = (up ? 2u : 0u) | (ext ? 1u : 0u), dwExtraInfo = Magic } } };
    }
    static INPUT Uni(char c, bool up) => new() { type = 1, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0, wScan = c, dwFlags = 4u | (up ? 2u : 0u), dwExtraInfo = Magic } } };
    static INPUT Wheel(bool horizontal, int amount) => new() { type = 0, u = new INPUTUNION { mi = new MOUSEINPUT { mouseData = amount, dwFlags = horizontal ? 0x1000u : 0x0800u, dwExtraInfo = Magic } } };
    // Input is never sent from inside a hook callback: the event would have to pass through the same hook while it is busy, and Windows
    // makes everyone wait for it to time out. A worker thread sends it instead, in order.
    static readonly System.Collections.Concurrent.BlockingCollection<List<INPUT>> _outbox = new();
    static Thread? _worker;
    static void Send(List<INPUT> l)
    {
        if (l.Count == 0) return;
        if (_worker == null)
            lock (_outbox)
                if (_worker == null)
                {
                    var t = new Thread(() => { foreach (var batch in _outbox.GetConsumingEnumerable()) { try { SendInput((uint)batch.Count, batch.ToArray(), Marshal.SizeOf<INPUT>()); } catch { } } }) { IsBackground = true, Name = "Perch input sender" };
                    t.Start(); _worker = t;
                }
        _outbox.Add(l);
    }
    // Presses the keys together (modifiers first) and lets them go again.
    public static void Press(params ushort[] vks)
    {
        var l = new List<INPUT>();
        foreach (var v in vks) l.Add(Key(v, false));
        for (int i = vks.Length - 1; i >= 0; i--) l.Add(Key(vks[i], true));
        Send(l);
    }

    // ── settings snapshot: the callbacks read plain fields, never the settings object ──
    static volatile bool _invV, _invH, _sideways, _linear, _smooth, _clickFilter, _keyDebounce, _guard, _guardAlt, _guardCtrlW, _guardHold, _snippets, _superCaps, _pauseGames, _hasButtons;
    static volatile int _sideVk, _clickMs, _keyMs, _holdMs;
    static double _speed = 1;
    static string _btnBack = "default", _btnFwd = "default", _btnMid = "default", _superTap = "none";
    static HashSet<string> _ignored = new(StringComparer.OrdinalIgnoreCase), _guardApps = new(StringComparer.OrdinalIgnoreCase);
    static List<(string Trigger, string Text)> _snips = new();
    static volatile bool _cleaning; static long _cleanUntil;
    public static bool Cleaning => _cleaning;

    static bool NeedMouse => _extMouse || _invV || _invH || _sideways || _linear || _smooth || _speed != 1 || _hasButtons || _clickFilter || _cleaning;
    static bool NeedKeys => _extKeys || _keyDebounce || _guard || _snippets || _superCaps || _cleaning;

    static Thread? _thread; static uint _tid; static bool _runMouse, _runKeys;
    static IntPtr _kb, _ms; static Proc? _kbProc, _msProc; static UIntPtr _timer;

    public static void Refresh()
    {
        var s = Settings.Current;
        _invV = s.ScrollInvert; _invH = s.ScrollInvertH; _sideways = s.ScrollSideways; _linear = s.ScrollLinear; _smooth = s.ScrollSmooth;
        _sideVk = s.ScrollSidewaysKey switch { "shift" => 0x10, "rctrl" => 0xA3, _ => 0x12 };
        _speed = Math.Clamp(s.ScrollSpeed, 25, 500) / 100.0;
        _btnBack = s.BtnBack; _btnFwd = s.BtnForward; _btnMid = s.BtnMiddle;
        _hasButtons = _btnBack != "default" || _btnFwd != "default" || _btnMid != "default";
        _clickFilter = s.ClickFilter; _clickMs = Math.Clamp(s.ClickFilterMs, 10, 200);
        _keyDebounce = s.KeyDebounce; _keyMs = Math.Clamp(s.KeyDebounceMs, 10, 200);
        _guard = s.QuitGuard; _guardHold = s.QuitGuardMode != "double"; _holdMs = Math.Clamp(s.QuitGuardHoldMs, 400, 3000);
        _guardAlt = s.QuitGuardKeys != "ctrlw"; _guardCtrlW = s.QuitGuardKeys != "altf4";
        _guardApps = new HashSet<string>(s.QuitGuardApps, StringComparer.OrdinalIgnoreCase);
        _snippets = s.SnippetExpand && s.TextSnippets.Any(t => t.Trigger.Length > 0 && t.Text.Length > 0);
        _snips = s.TextSnippets.Where(t => t.Trigger.Length > 0 && t.Text.Length > 0).Select(t => (t.Trigger, t.Text)).OrderByDescending(t => t.Trigger.Length).ToList();
        _superCaps = s.SuperKey == "caps"; _superTap = s.SuperTap;
        _pauseGames = s.InputPauseInGames;
        _ignored = new HashSet<string>(s.InputIgnoredApps, StringComparer.OrdinalIgnoreCase);
        MouseAccel.Apply(s.NoMouseAccel);
        RefreshExt(s);
        bool m = NeedMouse, k = NeedKeys;
        if (_thread != null && (m != _runMouse || k != _runKeys)) Stop();
        if (_thread == null && (m || k)) Start(m, k);
    }

    static void Start(bool mouse, bool keys)
    {
        _runMouse = mouse; _runKeys = keys;
        _thread = new Thread(Run) { IsBackground = true, Name = "Perch input hooks" };
        _thread.Start();
    }
    public static void Stop()
    {
        var t = _thread; _thread = null;
        if (t == null) return;
        PostThreadMessage(_tid, 0x0012, IntPtr.Zero, IntPtr.Zero); // WM_QUIT
        t.Join(800);
        _smoothTimer?.Dispose(); _smoothTimer = null; _pendV = _pendH = 0;
    }

    static void Run()
    {
        _tid = GetCurrentThreadId();
        var mod = GetModuleHandle(null);
        _kbProc = KbProc; _msProc = MouseProc;
        if (_runKeys) _kb = SetWindowsHookEx(13, _kbProc, mod, 0);
        if (_runMouse) _ms = SetWindowsHookEx(14, _msProc, mod, 0);
        _timer = SetTimer(IntPtr.Zero, (UIntPtr)1, 250, IntPtr.Zero);
        while (GetMessage(out var m, IntPtr.Zero, 0, 0) > 0)
        {
            if (m.message == 0x0113) { try { Tick(); } catch { } continue; }
            TranslateMessage(ref m); DispatchMessage(ref m);
        }
        KillTimer(IntPtr.Zero, _timer);
        if (_kb != IntPtr.Zero) UnhookWindowsHookEx(_kb); _kb = IntPtr.Zero;
        if (_ms != IntPtr.Zero) UnhookWindowsHookEx(_ms); _ms = IntPtr.Zero;
    }

    // four times a second, on the hook thread
    static int _ticks; static long _escSince;
    public static double EscProgress => _escSince == 0 ? 0 : Math.Clamp((Environment.TickCount64 - _escSince) / 3000.0, 0, 1);
    static void Tick()
    {
        long now = Environment.TickCount64;
        if (_cleaning)
        {
            // the way out never depends on the interface: hold Esc, or wait for the time limit
            if ((_escSince != 0 && now - _escSince >= 3000) || now >= _cleanUntil) { _cleaning = false; _escSince = 0; Svc.Ui(CleaningMode.Stop); }
        }
        if (++_ticks % 4 != 0) return;
        _exeCache.Clear();
        // a mouse hook is called for every movement, so it steps aside while a fullscreen game has the screen
        if (_runMouse && _pauseGames)
        {
            bool fs = !_cleaning && Native.ForegroundIsFullscreen(IntPtr.Zero);
            if (fs && _ms != IntPtr.Zero) { UnhookWindowsHookEx(_ms); _ms = IntPtr.Zero; }
            else if (!fs && _ms == IntPtr.Zero) _ms = SetWindowsHookEx(14, _msProc!, GetModuleHandle(null), 0);
        }
    }

    // ── which program is this for ──
    static readonly Dictionary<uint, string> _exeCache = new();
    static string ExeOf(IntPtr h)
    {
        try
        {
            if (h == IntPtr.Zero) return "";
            GetWindowThreadProcessId(h, out uint pid);
            if (_exeCache.TryGetValue(pid, out var n)) return n;
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            n = p.ProcessName + ".exe"; if (_exeCache.Count > 80) _exeCache.Clear(); _exeCache[pid] = n; return n;
        }
        catch { return ""; }
    }
    static bool IgnoredAt(int x, int y) => _ignored.Count > 0 && _ignored.Contains(ExeOf(GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), 2)));
    static bool IgnoredFront() => _ignored.Count > 0 && _ignored.Contains(ExeOf(GetForegroundWindow()));

    // ───────── mouse ─────────
    static IntPtr MouseProc(int code, IntPtr wp, IntPtr lp)
    {
        if (code >= 0)
        {
            try
            {
                int msg = wp.ToInt32();
                if (_cleaning) return (IntPtr)1;
                if (msg != 0x0200 || _gestureBusy) // plain movement is only looked at while a drag gesture is running
                {
                    var m = Marshal.PtrToStructure<MSL>(lp);
                    if (m.extra != Magic && (MouseExt(msg, m) || (msg != 0x0200 && Mouse(msg, m)))) return (IntPtr)1;
                }
            }
            catch { }
        }
        return CallNextHookEx(IntPtr.Zero, code, wp, lp);
    }

    static readonly long[] _lastDown = new long[4], _lastUp = new long[4]; static readonly bool[] _swallowUp = new bool[4];
    static bool Mouse(int msg, MSL m)
    {
        switch (msg)
        {
            case 0x020A: case 0x020E: // wheel, tilt wheel
                return Wheel(msg == 0x020E, (short)((m.data >> 16) & 0xFFFF), m);
            case 0x020B: case 0x020C: // side buttons
                {
                    int which = (int)((m.data >> 16) & 0xFFFF); // 1 back, 2 forward
                    string act = which == 1 ? _btnBack : which == 2 ? _btnFwd : "default";
                    if (act == "default" || IgnoredAt(m.x, m.y)) return false;
                    if (msg == 0x020B) Act(act);
                    return true;
                }
            case 0x0207: case 0x0208: // middle button
                {
                    if (_btnMid == "default") break;
                    if (IgnoredAt(m.x, m.y)) return false;
                    if (msg == 0x0207) Act(_btnMid);
                    return true;
                }
        }
        if (_clickFilter)
        {
            int b = msg is 0x0201 or 0x0202 ? 0 : msg is 0x0204 or 0x0205 ? 1 : msg is 0x0207 or 0x0208 ? 2 : -1;
            if (b >= 0)
            {
                bool down = msg is 0x0201 or 0x0204 or 0x0207; long now = Environment.TickCount64;
                if (down)
                {
                    if (now - _lastDown[b] < _clickMs || now - _lastUp[b] < _clickMs / 2) { _swallowUp[b] = true; return true; }
                    _lastDown[b] = now;
                }
                else
                {
                    if (_swallowUp[b]) { _swallowUp[b] = false; return true; }
                    _lastUp[b] = now;
                }
            }
        }
        if (_snippets && msg is 0x0201 or 0x0204 or 0x0207) _typed.Clear(); // a click moves the caret: forget what was typed
        return false;
    }

    // wheel: invert, sideways, speed, linear, smooth. Only real wheel notches (multiples of 120) are touched, never a touchpad's fine scrolling.
    static bool Wheel(bool tilt, int delta, MSL m)
    {
        if (delta == 0 || delta % 120 != 0) return false;
        bool any = (!tilt && _invV) || (tilt && _invH) || (_sideways && !tilt) || _linear || _smooth || _speed != 1;
        if (!any || IgnoredAt(m.x, m.y)) return false;
        bool horizontal = tilt;
        if (!tilt && _sideways && (GetAsyncKeyState(_sideVk) & 0x8000) != 0) { horizontal = true; delta = -delta; } // wheel up scrolls left, like Shift+wheel
        else if (!tilt && _invV) delta = -delta;
        if (tilt && _invH) delta = -delta;
        if (_linear) delta = Math.Sign(delta) * 120;
        double amount = delta * _speed;
        if (_smooth) { Glide(horizontal, amount); return true; }
        int a = (int)Math.Round(amount);
        if (a == 0) return true;
        Send(new List<INPUT> { Wheel(horizontal, a) });
        return true;
    }

    // smooth scrolling: the notch is split into many small steps over about a tenth of a second
    static double _pendV, _pendH; static Timer? _smoothTimer; static readonly object _glideLock = new();
    static void Glide(bool horizontal, double amount)
    {
        lock (_glideLock)
        {
            if (horizontal) _pendH += amount; else _pendV += amount;
            _smoothTimer ??= new Timer(_ => GlideStep(), null, 8, 8);
        }
    }
    static void GlideStep()
    {
        var l = new List<INPUT>();
        lock (_glideLock)
        {
            double v = Take(ref _pendV), h = Take(ref _pendH);
            if (v != 0) l.Add(Wheel(false, (int)v)); if (h != 0) l.Add(Wheel(true, (int)h));
            if (_pendV == 0 && _pendH == 0) { _smoothTimer?.Dispose(); _smoothTimer = null; }
        }
        Send(l);
    }
    static double Take(ref double pend)
    {
        if (pend == 0) return 0;
        double step = Math.Abs(pend) <= 12 ? pend : pend * 0.25;
        if (Math.Abs(step) < 1) step = Math.Sign(pend);
        step = Math.Truncate(step); pend -= step; if (Math.Abs(pend) < 1) { step += pend; pend = 0; step = Math.Truncate(step); }
        return step;
    }

    // ── what a remapped button does ──
    public static readonly (string Id, string Label)[] ButtonActions =
    {
        ("default", "Windows default"), ("none", "Nothing"), ("copy", "Copy"), ("paste", "Paste"), ("undo", "Undo"), ("closetab", "Close tab"), ("newtab", "New tab"),
        ("back", "Back"), ("forward", "Forward"), ("playpause", "Play or pause"), ("next", "Next track"), ("prev", "Previous track"), ("mute", "Mute"),
        ("volup", "Volume up"), ("voldown", "Volume down"), ("taskview", "Task view"), ("desktop", "Show desktop"), ("snip", "Snipping tool"),
        ("perch", "Open Perch"), ("commandbar", "Command bar"), ("radial", "Radial menu"), ("snippets", "Snippet menu"), ("switcher", "Window switcher"), ("shotarea", "Screenshot"), ("clipwindow", "Clipboard history"), ("recscreen", "Record the screen")
    };
    static void Act(string id)
    {
        switch (id)
        {
            case "none": return;
            case "copy": Press(0xA2, 0x43); return;
            case "paste": Press(0xA2, 0x56); return;
            case "undo": Press(0xA2, 0x5A); return;
            case "closetab": Press(0xA2, 0x57); return;
            case "newtab": Press(0xA2, 0x54); return;
            case "back": Press(0xA4, 0x25); return;
            case "forward": Press(0xA4, 0x27); return;
            case "playpause": Press(0xB3); return;
            case "next": Press(0xB0); return;
            case "prev": Press(0xB1); return;
            case "mute": Press(0xAD); return;
            case "volup": Press(0xAF); return;
            case "voldown": Press(0xAE); return;
            case "taskview": Press(0x5B, 0x09); return;
            case "desktop": Press(0x5B, 0x44); return;
            case "snip": Press(0x5B, 0xA0, 0x53); return;
            case "perch": Svc.Ui(() => IslandWindow.Instance.TogglePinned()); return;
            case "commandbar": Svc.Ui(() => CommandBar.Toggle()); return;
            case "radial": Svc.Ui(() => Radial.Toggle()); return;
            case "snippets": Svc.Ui(() => QuickPicks.Snippets()); return;
            case "switcher": Svc.Ui(() => Switcher.Toggle()); return;
            case "shotarea": Svc.Ui(() => Shot.Start("area")); return;
            case "clipwindow": Svc.Ui(() => QuickPicks.Clipboard_()); return;
            case "recscreen": Svc.Ui(() => Rec.Toggle()); return;
        }
    }

    // ───────── keyboard ─────────
    static IntPtr KbProc(int code, IntPtr wp, IntPtr lp)
    {
        if (code >= 0)
        {
            try
            {
                var k = Marshal.PtrToStructure<KBD>(lp);
                if (_cleaning)
                {
                    if (k.vk == 0x1B) { if ((k.flags & 0x80) != 0) _escSince = 0; else if (_escSince == 0) _escSince = Environment.TickCount64; }
                    return (IntPtr)1;
                }
                if (k.extra != Magic && (KeyboardExt(wp.ToInt32(), k) || Keyboard(wp.ToInt32(), k))) return (IntPtr)1;
            }
            catch { }
        }
        return CallNextHookEx(IntPtr.Zero, code, wp, lp);
    }

    static readonly bool[] _kDown = new bool[256], _kSwallowed = new bool[256]; static readonly long[] _kLastDown = new long[256], _kLastUp = new long[256];
    static bool _capsHeld, _capsChord, _capsMark; static long _capsAt;
    static readonly Dictionary<string, long> _guardAt = new(); static long _holdSince; static bool _holdFired;

    static bool Keyboard(int msg, KBD k)
    {
        bool up = (k.flags & 0x80) != 0; int vk = (int)k.vk; long now = Environment.TickCount64;
        bool mod = vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;

        // Super key: Caps Lock held acts as Ctrl+Alt+Shift, a quick tap does something small
        if (_superCaps)
        {
            if (vk == 0x14)
            {
                if (!up) { if (!_capsHeld) { _capsHeld = true; _capsChord = false; _capsAt = now; } return true; }
                _capsHeld = false;
                if (!_capsChord && now - _capsAt < 450) SuperTapAction();
                return true;
            }
            if (_capsHeld && !mod)
            {
                if (up) { if (_kSwallowed[vk & 255]) { _kSwallowed[vk & 255] = false; return true; } return false; }
                _capsChord = true; _kSwallowed[vk & 255] = true;
                Send(new List<INPUT> { Key(0xA2, false), Key(0xA4, false), Key(0xA0, false), Key((ushort)vk, false), Key((ushort)vk, true), Key(0xA0, true), Key(0xA4, true), Key(0xA2, true) });
                return true;
            }
        }

        // quit protection: Alt+F4 and Ctrl+W need a hold or a second press
        if (_guard && (vk == 0x73 || vk == 0x57))
        {
            if (!up)
            {
                bool alt = (GetAsyncKeyState(0x12) & 0x8000) != 0, ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                string? which = vk == 0x73 && alt && _guardAlt ? "Alt+F4" : vk == 0x57 && ctrl && !alt && _guardCtrlW ? "Ctrl+W" : null;
                if (which != null && GuardsFront())
                {
                    if (_guardHold)
                    {
                        _kSwallowed[vk & 255] = true;
                        if (_holdSince == 0) { _holdSince = now; _holdFired = false; Hint($"Keep holding {which} to close"); return true; }
                        if (!_holdFired && now - _holdSince >= _holdMs)
                        {
                            _holdFired = true;
                            Send(new List<INPUT> { Key((ushort)vk, false), Key((ushort)vk, true) });
                        }
                        return true;
                    }
                    if (_guardAt.TryGetValue(which, out var at) && now - at < 1500) { _guardAt.Remove(which); return false; }
                    _guardAt[which] = now; _kSwallowed[vk & 255] = true; Hint($"Press {which} again to close"); return true;
                }
            }
            else { _holdSince = 0; _holdFired = false; if (_kSwallowed[vk & 255]) { _kSwallowed[vk & 255] = false; return true; } }
        }

        // key debounce: a repeated letter that arrives too soon after the last one is the key bouncing
        if (_keyDebounce && !mod && IsTyping(vk))
        {
            if (!up)
            {
                if (_kDown[vk & 255]) return false; // the keyboard's own auto-repeat
                if (now - _kLastDown[vk & 255] < _keyMs || now - _kLastUp[vk & 255] < _keyMs / 2) { _kSwallowed[vk & 255] = true; return true; }
                _kDown[vk & 255] = true; _kLastDown[vk & 255] = now;
            }
            else
            {
                if (_kSwallowed[vk & 255]) { _kSwallowed[vk & 255] = false; return true; }
                _kDown[vk & 255] = false; _kLastUp[vk & 255] = now;
            }
        }

        if (_snippets && !up) Typed(vk, k);
        return false;
    }
    static bool IsTyping(int vk) => vk is >= 0x30 and <= 0x5A or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0x20 or 0x08 or 0x0D;
    static bool GuardsFront()
    {
        var exe = ExeOf(GetForegroundWindow());
        if (exe.Length == 0) return false; // Perch itself and the desktop are never guarded
        return _guardApps.Count == 0 ? exe != "explorer.exe" : _guardApps.Contains(exe);
    }
    static void Hint(string text) => Svc.Ui(() => IslandWindow.Instance.Flash("", text));
    static void SuperTapAction()
    {
        switch (_superTap)
        {
            case "escape": Press(0x1B); break;
            case "commandbar": Svc.Ui(() => CommandBar.Toggle()); break;
            case "island": Svc.Ui(() => IslandWindow.Instance.TogglePinned()); break;
            case "caps": Send(new List<INPUT> { Key(0x14, false), Key(0x14, true) }); break;
        }
    }

    // ───────── text snippets ─────────
    // The last few characters typed are kept in memory only, never stored or logged, and forgotten on a click, a window change or any shortcut.
    static readonly StringBuilder _typed = new(); static IntPtr _typedIn;
    static void Typed(int vk, KBD k)
    {
        var fg = GetForegroundWindow();
        if (fg != _typedIn) { _typed.Clear(); _typedIn = fg; }
        if (vk == 0x08) { if (_typed.Length > 0) _typed.Length--; return; }
        bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0, alt = (GetAsyncKeyState(0x12) & 0x8000) != 0, win = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
        if (ctrl || alt || win) { _typed.Clear(); return; }
        if (vk is 0x10 or 0xA0 or 0xA1 or 0x14) return; // Shift and Caps Lock alone type nothing
        var state = new byte[256];
        if ((GetAsyncKeyState(0x10) & 0x8000) != 0) state[0x10] = 0x80;
        if ((GetKeyState(0x14) & 1) != 0) state[0x14] = 1;
        GetWindowThreadProcessId(fg, out uint tid);
        var sb = new StringBuilder(8);
        int n = ToUnicodeEx((uint)vk, k.scan, state, sb, sb.Capacity, 4, GetKeyboardLayout(GetWindowThreadProcessId(fg, out _)));
        if (n != 1 || char.IsControl(sb[0])) { _typed.Clear(); return; }
        if (IgnoredFront()) { _typed.Clear(); return; }
        _typed.Append(sb[0]); if (_typed.Length > 48) _typed.Remove(0, _typed.Length - 48);
        var text = _typed.ToString();
        foreach (var (trigger, body) in _snips)
        {
            if (!text.EndsWith(trigger, StringComparison.Ordinal)) continue;
            _typed.Clear();
            _ = Task.Run(() => Expand(trigger, body));
            return;
        }
    }
    static void Expand(string trigger, string body)
    {
        try
        {
            Thread.Sleep(30); // let the last typed letter land first
            string text = Fill(body);
            var l = new List<INPUT>();
            for (int i = 0; i < trigger.Length; i++) { l.Add(Key(0x08, false)); l.Add(Key(0x08, true)); }
            foreach (var c in text)
            {
                if (c == '\r') continue;
                if (c == '\n') { l.Add(Key(0x0D, false)); l.Add(Key(0x0D, true)); }
                else if (c == '\t') { l.Add(Key(0x09, false)); l.Add(Key(0x09, true)); }
                else { l.Add(Uni(c, false)); l.Add(Uni(c, true)); }
            }
            Send(l);
        }
        catch { }
    }
    public static string Fill(string body)
    {
        var now = DateTime.Now;
        body = body.Replace("{date}", now.ToString("d")).Replace("{time}", UI.TimeText(now)).Replace("{datetime}", now.ToString("d") + " " + UI.TimeText(now));
        body = System.Text.RegularExpressions.Regex.Replace(body, @"\{\{(date|time|datetime)(?:-tz\(([^)]+)\))?(?::([^}]+))?\}\}", m =>
        {
            try
            {
                var when = now; if (m.Groups[2].Success) when = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(m.Groups[2].Value));
                string kind = m.Groups[1].Value, fmt = m.Groups[3].Success ? m.Groups[3].Value : "";
                if (fmt.Length > 0) return when.ToString(fmt);
                return kind == "date" ? when.ToString("d") : kind == "time" ? UI.TimeText(when) : when.ToString("d") + " " + UI.TimeText(when);
            }
            catch { return m.Value; }
        });
        body = body.Replace("{{clipboard}}", "{clipboard}");
        if (body.Contains("{clipboard}"))
        {
            string clip = "";
            try { clip = Application.Current.Dispatcher.Invoke(() => Clipboard.ContainsText() ? Clipboard.GetText() : "", System.Windows.Threading.DispatcherPriority.Send, default, TimeSpan.FromMilliseconds(400)); } catch { }
            body = body.Replace("{clipboard}", clip);
        }
        return body;
    }

    // ───────── cleaning mode ─────────
    public static void SetCleaning(bool on)
    {
        if (on) { _cleanUntil = Environment.TickCount64 + Math.Clamp(Settings.Current.CleaningMaxSeconds, 20, 1800) * 1000L; _escSince = 0; }
        _cleaning = on;
        if (!on) _escSince = 0;
        Refresh();
    }
}

// ───────────────────────── Pointer acceleration ─────────────────────────
public static class MouseAccel
{
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, int[] value, uint flags);
    public static void Apply(bool disable)
    {
        try
        {
            var s = Settings.Current; var cur = new int[3];
            if (!SystemParametersInfo(3, 0, cur, 0)) return;
            if (disable)
            {
                if (cur[2] == 0) return;
                if (s.MouseAccelPrev.Length == 0) { s.MouseAccelPrev = string.Join(",", cur); s.Save(); }
                SystemParametersInfo(4, 0, new[] { cur[0], cur[1], 0 }, 3);
            }
            else if (s.MouseAccelPrev.Length > 0)
            {
                var p = s.MouseAccelPrev.Split(',').Select(x => int.TryParse(x, out var v) ? v : 0).ToArray();
                if (p.Length == 3) SystemParametersInfo(4, 0, p, 3);
                s.MouseAccelPrev = ""; s.Save();
            }
        }
        catch { }
    }
}

// ───────────────────────── Cleaning mode ─────────────────────────
// Keyboard and mouse are switched off so the keys can be wiped. Holding Esc for three seconds ends it, and a time limit always does.
public static class CleaningMode
{
    static readonly List<Window> _wins = new(); static System.Windows.Threading.DispatcherTimer? _timer; static Border? _bar;
    public static bool Active => _wins.Count > 0;

    public static void Start()
    {
        if (Active) return;
        bool black = Settings.Current.CleaningStyle != "indicator";
        var barFill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        _bar = barFill;
        var track = new Border { Height = 4, Width = 220, CornerRadius = new CornerRadius(2), Background = B("#33FFFFFF"), Child = barFill, Margin = new Thickness(0, 14, 0, 0) };
        if (black)
        {
            foreach (var scr in System.Windows.Forms.Screen.AllScreens)
            {
                var card = Col(Ico("", 30, "#FFFFFF").With(x => x.HorizontalAlignment = HorizontalAlignment.Center),
                    T("Cleaning mode", 22, "#FFFFFF", true).With(x => { x.HorizontalAlignment = HorizontalAlignment.Center; x.Margin = new Thickness(0, 14, 0, 4); }),
                    T("Keyboard and mouse are off. Hold Esc for three seconds to finish.", 13, "#8E8E93").With(x => x.HorizontalAlignment = HorizontalAlignment.Center),
                    scr.Primary ? track : new Border());
                card.VerticalAlignment = VerticalAlignment.Center; card.HorizontalAlignment = HorizontalAlignment.Center;
                _wins.Add(Make(scr.Bounds.Left, scr.Bounds.Top, scr.Bounds.Width, scr.Bounds.Height, new Grid { Background = Brushes.Black, Children = { card } }, true));
            }
        }
        else
        {
            var scr = System.Windows.Forms.Screen.PrimaryScreen!;
            var pill = new Border
            {
                CornerRadius = new CornerRadius(20), Background = B("#E6141416"), BorderBrush = B("#3A3A3C"), BorderThickness = new Thickness(1), Padding = new Thickness(16, 10, 16, 10),
                Child = Col(Row(Ico("", 13, "#FFFFFF").M(0, 0, 8), T("Cleaning mode  ·  hold Esc to finish", 12, "#FFFFFF", true)), track.With(t => { t.Width = 200; t.Margin = new Thickness(0, 8, 0, 0); }))
            };
            _wins.Add(Make(scr.Bounds.Left + scr.Bounds.Width / 2 - 170, scr.Bounds.Top + 30, 340, 80, pill, false));
        }
        Hooks.SetCleaning(true);
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _timer.Tick += (_, _) => { if (_bar != null) _bar.Width = Hooks.EscProgress * (_bar.Parent is Border b ? b.Width : 220); };
        _timer.Start();
    }

    static Window Make(double x, double y, double w, double h, UIElement content, bool full)
    {
        var d = VisualTreeHelper.GetDpi(Application.Current.MainWindow ?? new Window());
        var win = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
            ResizeMode = ResizeMode.NoResize, Content = content, Cursor = System.Windows.Input.Cursors.None, WindowStartupLocation = WindowStartupLocation.Manual
        };
        win.SourceInitialized += (_, _) =>
        {
            var h2 = new System.Windows.Interop.WindowInteropHelper(win).Handle;
            double k = VisualTreeHelper.GetDpi(win).DpiScaleX;
            Native.SetWindowPos(h2, new IntPtr(-1), (int)x, (int)y, (int)w, (int)h, 0x0010);
        };
        win.Left = x / d.DpiScaleX; win.Top = y / d.DpiScaleY; win.Width = w / d.DpiScaleX; win.Height = h / d.DpiScaleY;
        win.Show();
        return win;
    }

    public static void Stop()
    {
        Hooks.SetCleaning(false);
        _timer?.Stop(); _timer = null; _bar = null;
        foreach (var w in _wins) try { w.Close(); } catch { }
        _wins.Clear();
        try { Svc.Notices.Add("Cleaning mode finished", "Keyboard and mouse are back.", ""); } catch { }
    }
}
