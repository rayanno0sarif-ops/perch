using System;
using System.Threading;
using System.Windows;

namespace Perch;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        using var mutex = new Mutex(true, "Perch.SingleInstance", out bool first);
        if (!first) return;

        Loc.Load();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            // one misbehaving tile must not take the whole island down
            try { Svc.Notices.Add("Something went wrong", e.Exception.Message, ""); } catch { }
            e.Handled = true;
        };

        var tip = new Style(typeof(System.Windows.Controls.ToolTip));
        tip.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, UI.B("#2C2C2E")));
        tip.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, UI.B("#FFFFFF")));
        tip.Setters.Add(new Setter(System.Windows.Controls.Control.BorderBrushProperty, UI.B("#48484A")));
        tip.Setters.Add(new Setter(System.Windows.Controls.Control.FontFamilyProperty, UI.Font));
        tip.Setters.Add(new Setter(System.Windows.Controls.Control.FontSizeProperty, 11.0));
        tip.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty, new Thickness(8, 4, 8, 4)));
        app.Resources[typeof(System.Windows.Controls.ToolTip)] = tip;

        var island = new IslandWindow();
        island.Show();

        var tray = new System.Windows.Forms.NotifyIcon { Text = "Perch", Visible = true, Icon = MakeIcon() };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open island", null, (_, _) => island.TogglePinned());
        menu.Items.Add("Settings", null, (_, _) => SettingsWindow.Open());
        menu.Items.Add("Quit", null, (_, _) => app.Shutdown());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => SettingsWindow.Open();

        NotifyServer.Restart();
        ToastMirror.Restart();
        Watch.Restart();
        Hooks.Refresh();
        GlassTaskbar.Restore(); GlassTaskbar.Sync(); LiveWallpaper.Sync(); DeskWidget.Sync();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => GlassTaskbar.Restore(); // never leave the Windows taskbar hidden
        Housekeeping.Start(); Tools2.Init(); Updater.Daily(); AgentWatch.Start(); Displays.Restore(); app.Dispatcher.BeginInvoke(new Action(Mascot.Sync), System.Windows.Threading.DispatcherPriority.Background);
        Svc.Clip.Load();
        DayClock.Sync();
        _ = Svc.Media.Init();
        _ = Svc.Weather.Refresh();
        Svc.Stats.Sample();

        app.Run();
        GlassTaskbar.Restore();
        Hooks.Stop();
        tray.Visible = false; tray.Dispose();
    }

    static System.Drawing.Icon MakeIcon()
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            var r = new System.Drawing.Rectangle(2, 9, 28, 14); int d = 14;
            path.AddArc(r.X, r.Y, d, d, 90, 180); path.AddArc(r.Right - d, r.Y, d, d, 270, 180); path.CloseFigure();
            g.FillPath(System.Drawing.Brushes.White, path);
        }
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }
}
