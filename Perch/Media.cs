using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static Perch.UI;

namespace Perch;

public partial class Settings
{
    public int RecFps { get; set; } = 15;
    public string RecQuality { get; set; } = "balanced";     // small | balanced | high
    public int RecCountdown { get; set; } = 3;
    public bool RecSystemAudio { get; set; }
    public bool RecMic { get; set; }
    public string RecFolder { get; set; } = "";              // empty is Videos\Perch
    public bool RecOpenEditor { get; set; } = true;
    public int RecGifWidth { get; set; } = 640;
    public int RecGifFps { get; set; } = 10;
    public bool RecPointer { get; set; } = true;
    public string CameraId { get; set; } = "";
    public bool CameraMirror { get; set; } = true;
}

// Records an area of the screen: frames are kept as pictures on disk, so the recording can be trimmed before it is written as a video or a GIF.
public static class Rec
{
    static volatile bool _on, _paused; static System.Drawing.Rectangle _area; static string _dir = ""; static int _frames; static DateTime _start; static Thread? _thread;
    static RecIndicator? _ind; static NAudio.Wave.WasapiLoopbackCapture? _loop; static NAudio.Wave.WaveFileWriter? _loopW; static NAudio.Wave.WaveInEvent? _mic; static NAudio.Wave.WaveFileWriter? _micW;
    public static bool Recording => _on;
    public static string VideoFolder => Settings.Current.RecFolder.Length > 0 ? Settings.Current.RecFolder : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Perch");

    public static void Toggle() { if (_on) Stop(); else Begin(); }
    // Pick what to record: an area, or a window, on a frozen picture of the screen.
    public static async void Begin()
    {
        if (_on) return;
        IslandWindow.Instance.Vanish(true); await Task.Delay(160);
        try
        {
            var vs = System.Windows.Forms.SystemInformation.VirtualScreen; var frozen = Shot.Grab(vs, false);
            new ShotSelect(frozen, vs, false, r => Start(r)).Show();
        }
        finally { IslandWindow.Instance.Vanish(false); }
    }
    static async void Start(System.Drawing.Rectangle area)
    {
        area.Width &= ~1; area.Height &= ~1; if (area.Width < 16 || area.Height < 16) return;
        _area = area; var s = Settings.Current;
        for (int i = s.RecCountdown; i > 0; i--) { Svc.Notices.Add("Recording in " + i, "", ""); await Task.Delay(1000); }
        _dir = Path.Combine(Path.GetTempPath(), "Perch-rec-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(_dir);
        _frames = 0; _paused = false; _on = true; _start = DateTime.Now;
        try
        {
            if (s.RecSystemAudio) { _loop = new NAudio.Wave.WasapiLoopbackCapture(); _loopW = new NAudio.Wave.WaveFileWriter(Path.Combine(_dir, "sys.wav"), _loop.WaveFormat); _loop.DataAvailable += (_, e) => { if (!_paused) _loopW?.Write(e.Buffer, 0, e.BytesRecorded); }; _loop.StartRecording(); }
            if (s.RecMic) { _mic = new NAudio.Wave.WaveInEvent { WaveFormat = new NAudio.Wave.WaveFormat(44100, 16, 1) }; _micW = new NAudio.Wave.WaveFileWriter(Path.Combine(_dir, "mic.wav"), _mic.WaveFormat); _mic.DataAvailable += (_, e) => { if (!_paused) _micW?.Write(e.Buffer, 0, e.BytesRecorded); }; _mic.StartRecording(); }
        }
        catch { Svc.Notices.Add("Audio could not be recorded", "The picture is still being recorded.", ""); }
        int fps = Math.Clamp(s.RecFps, 5, 30);
        _thread = new Thread(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew(); long next = 0; long frameMs = 1000 / fps;
            var codec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
            var ep = new System.Drawing.Imaging.EncoderParameters(1); ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
            while (_on)
            {
                if (sw.ElapsedMilliseconds < next) { Thread.Sleep(1); continue; }
                next += frameMs;
                if (_paused) { next = sw.ElapsedMilliseconds + frameMs; continue; }
                try { using var b = Shot.Grab(_area, Settings.Current.RecPointer); b.Save(Path.Combine(_dir, $"f{_frames:D6}.jpg"), codec, ep); _frames++; }
                catch { }
                if (_frames > 30 * 60 * 10) break;
            }
        }) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        _ind = new RecIndicator(); _ind.Show();
    }
    public static void Pause() { _paused = !_paused; }
    public static bool Paused => _paused;
    public static TimeSpan Elapsed => TimeSpan.FromSeconds(_frames / (double)Math.Clamp(Settings.Current.RecFps, 5, 30));
    public static void Stop()
    {
        if (!_on) return; _on = false; _thread?.Join(2000);
        try { _loop?.StopRecording(); _loopW?.Dispose(); _mic?.StopRecording(); _micW?.Dispose(); } catch { }
        _loop = null; _mic = null; _loopW = null; _micW = null;
        _ind?.Close(); _ind = null;
        if (_frames < 2) { Svc.Notices.Add("Nothing was recorded", "", ""); try { Directory.Delete(_dir, true); } catch { } return; }
        new RecEditor(_dir, _frames, Math.Clamp(Settings.Current.RecFps, 5, 30), _area.Width, _area.Height, File.Exists(Path.Combine(_dir, "sys.wav")), File.Exists(Path.Combine(_dir, "mic.wav"))).Show();
    }

    // ── writing the video ──
    static byte[] FrameBytes(string file, int w, int h)
    {
        using var src = new System.Drawing.Bitmap(file);
        using var b = src.Width == w && src.Height == h ? (System.Drawing.Bitmap)src.Clone() : new System.Drawing.Bitmap(src, w, h);
        var bd = b.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bytes = new byte[bd.Stride * h]; System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, bytes, 0, bytes.Length); b.UnlockBits(bd); return bytes;
    }
    public static async Task<bool> WriteMp4(string dir, int from, int to, int fps, int w, int h, string outFile, bool sys, bool mic, double speed, IProgress<double>? progress)
    {
        try
        {
            var vp = Windows.Media.MediaProperties.VideoEncodingProperties.CreateUncompressed(Windows.Media.MediaProperties.MediaEncodingSubtypes.Bgra8, (uint)w, (uint)h);
            vp.FrameRate.Numerator = (uint)fps; vp.FrameRate.Denominator = 1;
            var mss = new Windows.Media.Core.MediaStreamSource(new Windows.Media.Core.VideoStreamDescriptor(vp)) { BufferTime = TimeSpan.Zero, CanSeek = false };
            int i = from; var step = TimeSpan.FromSeconds(1.0 / (fps * Math.Max(0.25, speed)));
            mss.SampleRequested += (s, a) =>
            {
                if (i > to) { a.Request.Sample = null; return; }
                var bytes = FrameBytes(Path.Combine(dir, $"f{i:D6}.jpg"), w, h);
                var smp = Windows.Media.Core.MediaStreamSample.CreateFromBuffer(bytes.AsBuffer(), TimeSpan.FromTicks(step.Ticks * (i - from))); smp.Duration = step;
                a.Request.Sample = smp; i++; progress?.Report((i - from) / (double)(to - from + 1));
            };
            string tmp = sys || mic ? Path.Combine(dir, "video-only.mp4") : outFile;
            var q = Settings.Current.RecQuality; var prof = Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp4(Windows.Media.MediaProperties.VideoEncodingQuality.HD1080p);
            prof.Audio = null; prof.Video!.Width = (uint)w; prof.Video.Height = (uint)h; prof.Video.FrameRate.Numerator = (uint)fps; prof.Video.FrameRate.Denominator = 1;
            prof.Video.Bitrate = (uint)Math.Clamp((long)w * h * fps / (q == "small" ? 18 : q == "high" ? 5 : 9), 400_000, 40_000_000);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite))
            {
                var tc = new Windows.Media.Transcoding.MediaTranscoder { HardwareAccelerationEnabled = true };
                var prep = await tc.PrepareMediaStreamSourceTranscodeAsync(mss, fs.AsRandomAccessStream(), prof);
                if (!prep.CanTranscode) return false;
                await prep.TranscodeAsync();
            }
            if (tmp != outFile)
            {
                // put the sound on it
                var comp = new Windows.Media.Editing.MediaComposition();
                var clip = await Windows.Media.Editing.MediaClip.CreateFromFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(tmp)); comp.Clips.Add(clip);
                double t0 = (from) / (double)fps, t1 = (to + 1) / (double)fps;
                foreach (var wav in new[] { sys ? "sys.wav" : "", mic ? "mic.wav" : "" }.Where(x => x.Length > 0))
                {
                    var f = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.Combine(dir, wav));
                    var track = await Windows.Media.Editing.BackgroundAudioTrack.CreateFromFileAsync(f);
                    track.TrimTimeFromStart = TimeSpan.FromSeconds(t0); comp.BackgroundAudioTracks.Add(track);
                }
                var outF = await (await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(outFile)!)).CreateFileAsync(Path.GetFileName(outFile), Windows.Storage.CreationCollisionOption.ReplaceExisting);
                var res = await comp.RenderToFileAsync(outF, Windows.Media.Editing.MediaTrimmingPreference.Fast, prof);
                if (res != Windows.Media.Transcoding.TranscodeFailureReason.None) { File.Copy(tmp, outFile, true); }
            }
            return File.Exists(outFile);
        }
        catch { return false; }
    }
    public static bool WriteGif(string dir, int from, int to, int fps, int w, int h, string outFile, int gifFps, int gifWidth)
    {
        try
        {
            int step = Math.Max(1, (int)Math.Round(fps / (double)Math.Max(1, gifFps))); int gw = Math.Min(w, gifWidth), gh = Math.Max(2, h * gw / w);
            using var fs = File.Create(outFile); var gif = new GifWriter(fs, gw, gh, (int)Math.Round(100.0 * step / fps));
            for (int i = from; i <= to; i += step) { using var src = new System.Drawing.Bitmap(Path.Combine(dir, $"f{i:D6}.jpg")); using var b = new System.Drawing.Bitmap(src, gw, gh); gif.Add(b); }
            gif.Finish(); return true;
        }
        catch { return false; }
    }
}

// A GIF89a writer with one fixed 256-colour palette (3 bits red, 3 green, 2 blue), dithered so gradients do not band.
public class GifWriter
{
    readonly Stream _s; readonly int _w, _h, _delay; bool _first = true;
    static readonly int[,] Bayer = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };
    public GifWriter(Stream s, int w, int h, int delayCs) { _s = s; _w = w; _h = h; _delay = Math.Max(2, delayCs); }
    void W(params byte[] b) => _s.Write(b, 0, b.Length);
    void W16(int v) { W((byte)(v & 255), (byte)(v >> 8)); }
    public void Add(System.Drawing.Bitmap bmp)
    {
        if (_first)
        {
            W((byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a'); W16(_w); W16(_h); W(0xF7, 0, 0);
            for (int i = 0; i < 256; i++) W((byte)(((i >> 5) & 7) * 255 / 7), (byte)(((i >> 2) & 7) * 255 / 7), (byte)((i & 3) * 255 / 3));
            W(0x21, 0xFF, 0x0B); foreach (var c in "NETSCAPE2.0") W((byte)c); W(3, 1, 0, 0, 0); _first = false;
        }
        var bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, _w, _h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var px = new byte[bd.Stride * _h]; System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, px, 0, px.Length); bmp.UnlockBits(bd);
        var idx = new byte[_w * _h];
        for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
            {
                int o = y * bd.Stride + x * 4; int t = Bayer[y & 3, x & 3] * 255 / 16 - 8;
                int b = Math.Clamp(px[o] + t / 2, 0, 255), g = Math.Clamp(px[o + 1] + t, 0, 255), r = Math.Clamp(px[o + 2] + t, 0, 255);
                idx[y * _w + x] = (byte)(((r * 7 + 127) / 255) << 5 | ((g * 7 + 127) / 255) << 2 | (b * 3 + 127) / 255);
            }
        W(0x21, 0xF9, 4, 0x04); W16(_delay); W(0, 0);
        W(0x2C); W16(0); W16(0); W16(_w); W16(_h); W(0);
        Lzw(idx, 8);
    }
    void Lzw(byte[] data, int minBits)
    {
        W((byte)minBits); var outBuf = new List<byte>(); int clear = 1 << minBits, eoi = clear + 1, next = eoi + 1, bits = minBits + 1; uint acc = 0; int nacc = 0;
        var dict = new Dictionary<int, int>();
        void Emit(int code) { acc |= (uint)code << nacc; nacc += bits; while (nacc >= 8) { outBuf.Add((byte)(acc & 255)); acc >>= 8; nacc -= 8; } }
        Emit(clear); int cur = data[0];
        for (int i = 1; i < data.Length; i++)
        {
            int c = data[i], key = (cur << 8) | c;
            if (dict.TryGetValue(key, out int v)) { cur = v; continue; }
            Emit(cur);
            if (next < 4096) { dict[key] = next++; if (next > (1 << bits) && bits < 12) bits++; }
            else { Emit(clear); dict.Clear(); next = eoi + 1; bits = minBits + 1; }
            cur = c;
        }
        Emit(cur); Emit(eoi); if (nacc > 0) outBuf.Add((byte)(acc & 255));
        for (int i = 0; i < outBuf.Count; i += 255) { int n = Math.Min(255, outBuf.Count - i); _s.WriteByte((byte)n); _s.Write(outBuf.ToArray(), i, n); }
        W(0);
    }
    public void Finish() { W(0x3B); _s.Flush(); }
}

public class RecIndicator : Window
{
    readonly TextBlock _time = T("0:00", 12, "#FFFFFF", true, true); readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(300) }; readonly Border _dot = new() { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = B("#FF453A") };
    public RecIndicator()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight; Title = "Perch recording";
        var pause = Btn(T("Pause", 11, "#FFFFFF", true), () => Rec.Pause(), "#26FFFFFF", 9, new Thickness(10, 4, 10, 4)); var stop = Btn(T("Stop", 11, "#000000", true), () => Rec.Stop(), "#FFFFFF", 9, new Thickness(12, 4, 12, 4));
        Content = new Border { Background = B("#F0161618"), BorderBrush = Glass.Rim, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 7, 8, 7), Margin = new Thickness(8), Child = Row(_dot.With(d => d.VerticalAlignment = VerticalAlignment.Center), _time.M(9, 0, 12, 0).With(x => x.VerticalAlignment = VerticalAlignment.Center), pause.M(0, 0, 6, 0), stop) };
        var wa = SystemParameters.WorkArea; Left = wa.Left + (wa.Width - 220) / 2; Top = wa.Top + 14;
        MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not TextBlock) { try { DragMove(); } catch { } } };
        _t.Tick += (_, _) => { var e = Rec.Elapsed; _time.Text = $"{(int)e.TotalMinutes}:{e.Seconds:00}"; _dot.Opacity = Rec.Paused ? 0.35 : _dot.Opacity > 0.5 ? 0.45 : 1; };
        _t.Start(); Closed += (_, _) => _t.Stop();
        SourceInitialized += (_, _) => { if (Settings.Current.HideFromCapture) { } };
    }
}

// After recording: trim, then save as video or GIF.
public class RecEditor : Window
{
    readonly string _dir; readonly int _n, _fps, _w, _h; readonly bool _sys, _mic; int _from, _to; readonly TextBlock _info = T("", 11, Dim); readonly Image _preview = new() { Stretch = Stretch.Uniform }; readonly TextBlock _status = T("", 11, Dim); bool _busy; double _speed = 1;
    public RecEditor(string dir, int n, int fps, int w, int h, bool sys, bool mic)
    {
        _dir = dir; _n = n; _fps = fps; _w = w; _h = h; _sys = sys; _mic = mic; _from = 0; _to = n - 1;
        Title = "Perch recording editor"; Width = 760; Height = 600; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen; UseLayoutRounding = true;
        var head = MakeGrid("*,auto", "auto"); head.Margin = new Thickness(18, 12, 10, 4); head.Background = Brushes.Transparent;
        head.Children.Add(T("Recording", 13, "#FFFFFF", true).At(0, 0)); head.Children.Add(IconBtn("", () => Discard(true), 10, "#C7C7CC", 28).At(0, 1));
        head.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        var startS = new MiniSlider(3) { Width = 300, Value = 0 }; var endS = new MiniSlider(3) { Width = 300, Value = 1 };
        startS.Changed += v => { _from = Math.Min((int)(v * (_n - 1)), _to - 1); Show_(_from); Info(); }; endS.Changed += v => { _to = Math.Max((int)(v * (_n - 1)), _from + 1); Show_(_to); Info(); };
        var speedRow = new Segmented(new[] { "0.5×", "1×", "1.5×", "2×" }, 1, i => _speed = new[] { 0.5, 1, 1.5, 2 }[i]);
        var gifRow = new Segmented(new[] { "Small", "Medium", "Large" }, Settings.Current.RecGifWidth <= 480 ? 0 : Settings.Current.RecGifWidth >= 800 ? 2 : 1, i => { Settings.Current.RecGifWidth = new[] { 480, 640, 960 }[i]; Settings.Current.Save(); });
        var opts = Col(Row(T("Start", 11, Dim).M(0, 0, 10).With(x => x.VerticalAlignment = VerticalAlignment.Center), startS, T("End", 11, Dim).M(24, 0, 10).With(x => x.VerticalAlignment = VerticalAlignment.Center), endS),
            Row(T("Speed", 11, Dim).M(0, 12, 12).With(x => x.VerticalAlignment = VerticalAlignment.Center), speedRow, T("GIF size", 11, Dim).M(24, 12, 12).With(x => x.VerticalAlignment = VerticalAlignment.Center), gifRow));
        var actions = Row(Pill("Save video", () => Export(false), true), Pill("Save GIF", () => Export(true)).M(6), Pill("Copy video", async () => { var f = await ExportTemp(); if (f != null) { var c = new System.Collections.Specialized.StringCollection { f }; try { Clipboard.SetFileDropList(c); Svc.Notices.Add("Recording copied", "Paste it into a chat or a folder.", ""); } catch { } } }).M(6), Pill("Delete", () => Discard(true)).M(6));
        var root = MakeGrid("*", "auto,*,auto,auto,auto,auto");
        root.Children.Add(head.At(0, 0)); root.Children.Add(Box(_preview, "#14FFFFFF", 12, new Thickness(6)).M(18, 4, 18, 8).At(1, 0));
        root.Children.Add(_info.M(20, 0, 20, 6).At(2, 0)); root.Children.Add(opts.M(20, 0, 20, 10).At(3, 0)); root.Children.Add(actions.M(20, 0, 20, 4).At(4, 0)); root.Children.Add(_status.M(20, 4, 20, 14).At(5, 0));
        Content = new Border { Background = B("#F01C1C1E"), CornerRadius = new CornerRadius(16), BorderBrush = Glass.Rim, BorderThickness = new Thickness(1), Child = root, Margin = new Thickness(10), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 26, ShadowDepth = 5, Opacity = 0.5 } };
        Show_(0); Info();
        Loaded += (_, _) => { if (!Settings.Current.RecOpenEditor) { } };
    }
    void Show_(int i)
    {
        try { var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 700; bi.UriSource = new Uri(Path.Combine(_dir, $"f{i:D6}.jpg")); bi.EndInit(); bi.Freeze(); _preview.Source = bi; } catch { }
    }
    void Info() { double len = (_to - _from + 1) / (double)_fps; _info.Text = $"{_w} × {_h} · {_fps} fps · {len:0.0} s selected of {_n / (double)_fps:0.0} s" + (_sys ? " · computer sound" : "") + (_mic ? " · microphone" : ""); }
    async Task<string?> ExportTemp()
    {
        if (_busy) return null; _busy = true; _status.Text = "Preparing the video…";
        string f = Path.Combine(Path.GetTempPath(), "Perch recording " + DateTime.Now.ToString("HH.mm.ss") + ".mp4");
        bool ok = await Rec.WriteMp4(_dir, _from, _to, _fps, _w, _h, f, _sys, _mic, _speed, new Progress<double>(p => _status.Text = $"Writing the video… {p * 100:0}%"));
        _busy = false; _status.Text = ok ? "Ready." : "The video could not be written."; return ok ? f : null;
    }
    async void Export(bool gif)
    {
        if (_busy) return; Directory.CreateDirectory(Rec.VideoFolder);
        string stem = "Recording " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss"); string f = Path.Combine(Rec.VideoFolder, stem + (gif ? ".gif" : ".mp4"));
        _busy = true; _status.Text = gif ? "Writing the GIF…" : "Writing the video…";
        bool ok = gif ? await Task.Run(() => Rec.WriteGif(_dir, _from, _to, _fps, _w, _h, f, Settings.Current.RecGifFps, Settings.Current.RecGifWidth))
            : await Rec.WriteMp4(_dir, _from, _to, _fps, _w, _h, f, _sys, _mic, _speed, new Progress<double>(p => _status.Text = $"Writing the video… {p * 100:0}%"));
        _busy = false;
        if (!ok) { _status.Text = "It could not be written."; return; }
        _status.Text = "Saved to " + f; Svc.Notices.Add("Recording saved", Path.GetFileName(f), ""); Svc.AddToShelf(new[] { f });
    }
    void Discard(bool close) { if (_busy) return; try { Directory.Delete(_dir, true); } catch { } if (close) Close(); }
}

// A mirror to check yourself before a call.
public class CameraWindow : Window
{
    static CameraWindow? _open; readonly Image _img = new() { Stretch = Stretch.UniformToFill }; Windows.Media.Capture.MediaCapture? _cap; Windows.Media.Capture.Frames.MediaFrameReader? _reader; WriteableBitmap? _wb; readonly TextBlock _msg = T("Starting the camera…", 12, "#FFFFFF");
    public static void Toggle() { if (_open != null) { _open.Close(); return; } _open = new CameraWindow(); _open.Show(); }
    CameraWindow()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; Width = 360; Height = 290; Title = "Perch camera";
        var wa = SystemParameters.WorkArea; Left = wa.Right - Width - 30; Top = wa.Bottom - Height - 30;
        if (Settings.Current.CameraMirror) _img.RenderTransform = new ScaleTransform(-1, 1); _img.RenderTransformOrigin = new Point(0.5, 0.5);
        _msg.HorizontalAlignment = HorizontalAlignment.Center; _msg.VerticalAlignment = VerticalAlignment.Center; _msg.TextWrapping = TextWrapping.Wrap; _msg.TextAlignment = TextAlignment.Center; _msg.Margin = new Thickness(20);
        var close = IconBtn("", Close, 10, "#FFFFFF", 28).With(b => { b.HorizontalAlignment = HorizontalAlignment.Right; b.VerticalAlignment = VerticalAlignment.Top; b.Margin = new Thickness(0, 6, 6, 0); });
        var flip = IconBtn("", () => { Settings.Current.CameraMirror = !Settings.Current.CameraMirror; Settings.Current.Save(); _img.RenderTransform = Settings.Current.CameraMirror ? new ScaleTransform(-1, 1) : Transform.Identity; }, 11, "#FFFFFF", 28).With(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.VerticalAlignment = VerticalAlignment.Top; b.Margin = new Thickness(6, 6, 0, 0); b.ToolTip = "Mirror"; });
        var next = IconBtn("", async () => { await Stop(); Settings.Current.CameraId = "next"; await StartAsync(true); }, 11, "#FFFFFF", 28).With(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.VerticalAlignment = VerticalAlignment.Top; b.Margin = new Thickness(40, 6, 0, 0); b.ToolTip = "Another camera"; });
        var g = new Grid(); g.Children.Add(_img); g.Children.Add(_msg); g.Children.Add(close); g.Children.Add(flip); g.Children.Add(next);
        Content = new Border { Margin = new Thickness(8), CornerRadius = new CornerRadius(22), Background = B("#101012"), BorderBrush = Glass.Rim, BorderThickness = new Thickness(1.5), Child = g, ClipToBounds = true, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.55 } };
        MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not Border b || b.Cursor != Cursors.Hand) { try { DragMove(); } catch { } } };
        Loaded += async (_, _) => await StartAsync(false);
        Closed += async (_, _) => { _open = null; await Stop(); };
    }
    async Task Stop() { try { if (_reader != null) { await _reader.StopAsync(); _reader.Dispose(); } _cap?.Dispose(); } catch { } _reader = null; _cap = null; }
    async Task StartAsync(bool cycle)
    {
        try
        {
            var groups = (await Windows.Media.Capture.Frames.MediaFrameSourceGroup.FindAllAsync()).Where(g => g.SourceInfos.Any(i => i.SourceKind == Windows.Media.Capture.Frames.MediaFrameSourceKind.Color)).ToList();
            if (groups.Count == 0) { _msg.Text = "No camera found."; return; }
            int at = Math.Max(0, groups.FindIndex(g => g.Id == Settings.Current.CameraId)); if (cycle) at = (at + 1) % groups.Count;
            var grp = groups[Math.Min(at, groups.Count - 1)]; Settings.Current.CameraId = grp.Id; Settings.Current.Save();
            _cap = new Windows.Media.Capture.MediaCapture();
            await _cap.InitializeAsync(new Windows.Media.Capture.MediaCaptureInitializationSettings { SourceGroup = grp, SharingMode = Windows.Media.Capture.MediaCaptureSharingMode.SharedReadOnly, MemoryPreference = Windows.Media.Capture.MediaCaptureMemoryPreference.Cpu, StreamingCaptureMode = Windows.Media.Capture.StreamingCaptureMode.Video });
            var src = _cap.FrameSources.Values.First(x => x.Info.SourceKind == Windows.Media.Capture.Frames.MediaFrameSourceKind.Color);
            _reader = await _cap.CreateFrameReaderAsync(src, Windows.Media.MediaProperties.MediaEncodingSubtypes.Bgra8);
            _reader.FrameArrived += (s, e) =>
            {
                using var f = s.TryAcquireLatestFrame(); var sb = f?.VideoMediaFrame?.SoftwareBitmap; if (sb == null) return;
                using var conv = Windows.Graphics.Imaging.SoftwareBitmap.Convert(sb, Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
                int w = conv.PixelWidth, h = conv.PixelHeight; var buf = new byte[w * h * 4]; conv.CopyToBuffer(buf.AsBuffer());
                Dispatcher.BeginInvoke(() =>
                {
                    if (_wb == null || _wb.PixelWidth != w || _wb.PixelHeight != h) { _wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null); _img.Source = _wb; _msg.Visibility = Visibility.Collapsed; }
                    _wb.WritePixels(new Int32Rect(0, 0, w, h), buf, w * 4, 0);
                });
            };
            await _reader.StartAsync();
        }
        catch (Exception e) { _msg.Text = e.Message.Contains("denied", StringComparison.OrdinalIgnoreCase) || e.HResult == unchecked((int)0x80070005) ? "Windows is blocking the camera for desktop apps. Allow it in Settings, Privacy, Camera." : "The camera could not start. " + e.Message; }
    }
}
