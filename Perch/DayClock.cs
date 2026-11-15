using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Perch;

// Day, date and time drawn straight onto the desktop, behind every other window.
public class DayClock : Window
{
    static DayClock? _instance;
    static Settings S => Settings.Current;
    public static void Sync()
    {
        if (!S.DayClock) { _instance?.Close(); _instance = null; return; }
        if (_instance == null) { _instance = new DayClock(); _instance.Show(); }
        _instance.ApplyScale();
    }

    readonly TextBlock _day, _date, _time;
    readonly ScaleTransform _scale = new(1, 1);
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    IntPtr _hwnd;

    DayClock()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; ShowInTaskbar = false; ShowActivated = false;
        ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight; Title = "Perch clock";
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)); // nearly invisible, but lets you grab between the letters
        var light = new FontFamily("Segoe UI Variable Display, Segoe UI");
        TextBlock Make(double size, FontWeight w) => new()
        {
            FontFamily = light, FontSize = size, FontWeight = w, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center
        };
        _day = Make(64, FontWeights.Light);
        _date = Make(17, FontWeights.SemiBold); _date.Margin = new Thickness(0, 4, 0, 0);
        _time = Make(15, FontWeights.SemiBold); _time.Margin = new Thickness(0, 10, 0, 0);
        var sp = new StackPanel { Margin = new Thickness(24), LayoutTransform = _scale, Cursor = Cursors.SizeAll };
        sp.Children.Add(_day); sp.Children.Add(_date); sp.Children.Add(_time);
        Content = sp;

        if (S.ClockX >= 0 && S.ClockY >= 0) { Left = S.ClockX; Top = S.ClockY; }
        else { Left = SystemParameters.PrimaryScreenWidth - 700; Top = 140; }

        MouseLeftButtonDown += (_, _) =>
        {
            try { DragMove(); } catch { }
            S.ClockX = Left; S.ClockY = Top; S.Save();
            ToBottom();
        };
        MouseWheel += (_, e) =>
        {
            S.ClockScale = Math.Clamp(S.ClockScale + (e.Delta > 0 ? 0.05 : -0.05), 0.5, 2.5);
            S.Save(); ApplyScale();
        };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(_hwnd, -20, Native.GetWindowLong(_hwnd, -20) | 0x80 | 0x08000000); // tool window, never takes focus
            ToBottom();
        };
        ContentRendered += (_, _) => ToBottom(); // showing a window raises it; put it back behind everything
        _timer.Tick += (_, _) => Update();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
        Update();
    }
    void ApplyScale()
    {
        _scale.ScaleX = _scale.ScaleY = Math.Clamp(S.ClockScale, 0.5, 2.5);
        var ink = S.ClockHue < 0 ? Brushes.White : new SolidColorBrush(Glass.FromHue(S.ClockHue, 0.55, 1));
        _day.Foreground = _date.Foreground = _time.Foreground = ink;
    }
    void ToBottom() { if (_hwnd != IntPtr.Zero) Native.SetWindowPos(_hwnd, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }
    void Update()
    {
        var now = DateTime.Now;
        _day.Text = string.Join("  ", now.ToString("dddd").ToUpper().ToCharArray());
        _date.Text = now.ToString("d MMMM yyyy").ToUpper();
        _time.Text = UI.TimeText(now);
    }
}
