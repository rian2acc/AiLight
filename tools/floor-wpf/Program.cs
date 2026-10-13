using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Floor;

/// <summary>
/// Messwerkzeug: der **Startboden** von WPF unter .NET 8. Zeigt ein Fenster wie
/// die Leiste (randlos, transparent, abgerundet) und meldet, wie lange das
/// gedauert hat. Damit lässt sich sagen, welcher Anteil der Startzeit nicht von
/// AIlight selbst kommt.
///
/// Aufruf:  floor.exe [--shadow] [--hold]
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var shadow = args.Contains("--shadow");
        var hold = args.Contains("--hold");
        var processStart = Process.GetCurrentProcess().StartTime;

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            Width = 680,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 200,
            Top = 120,
        };

        var border = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x1E, 0x1F, 0x22)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Child = new TextBlock
            {
                Text = "Boden",
                Margin = new Thickness(16, 14, 16, 14),
                Foreground = Brushes.White,
                FontSize = 17,
            },
        };

        if (shadow)
        {
            border.Effect = new DropShadowEffect
            {
                BlurRadius = 16,
                Color = Colors.Black,
                Opacity = 0.45,
                ShadowDepth = 3,
            };
        }

        window.Content = border;

        app.Startup += (_, _) =>
        {
            window.Show();
            var untilShow = (DateTime.Now - processStart).TotalMilliseconds;
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var untilRendered = (DateTime.Now - processStart).TotalMilliseconds;
                Console.WriteLine($"floor: modus={(shadow ? "schatten" : "ohne")} "
                    + $"show_ms={untilShow:0} gerendert_ms={untilRendered:0}");
                if (!hold)
                {
                    app.Shutdown();
                }
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        };

        return app.Run();
    }
}
