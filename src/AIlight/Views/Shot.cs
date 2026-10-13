using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AIlight.Views;

/// <summary>
/// Bildschirmfotos der eigenen Fenster — nicht abfotografiert, sondern direkt aus
/// dem Aufbau gerendert. Damit lässt sich die Gestaltung prüfen und ansehen, ohne
/// dass jemand die Fenster von Hand öffnen muss (Aufruf: <c>--shot ordner</c>).
/// </summary>
public static class Shot
{
    /// <summary>Wartet, bis WPF wirklich gezeichnet hat.</summary>
    public static void Settle(DispatcherObject target)
    {
        target.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        target.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        target.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        target.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }

    /// <summary>
    /// Ein Fenster außerhalb des Bildschirms aufbauen, damit es messbar ist.
    /// Es klaut dabei keinen Fokus (ShowActivated bleibt aus).
    /// </summary>
    public static void Show(Window window, Action? prepare = null)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Left = -20000;
        window.Top = -20000;
        prepare?.Invoke();
        window.Show();
        Settle(window);
    }

    /// <summary>Ein Fenster außerhalb des Bildschirms zeigen, rendern, wieder schließen.</summary>
    public static void SaveWindow(Window window, string path, Action? prepare = null)
    {
        Show(window, prepare);

        var width = (int)Math.Ceiling(window.ActualWidth);
        var height = (int)Math.Ceiling(window.ActualHeight);
        Write(Render(window, width, height), path);

        window.Hide();
        window.Close();
    }

    /// <summary>
    /// Einen Teilbaum ohne Fenster rendern: herausnehmen, messen, anordnen, zeichnen.
    /// So entsteht ein Bild der Leiste, ohne dass sie kurz auf dem Bildschirm blitzt.
    /// </summary>
    public static void SaveElement(FrameworkElement element, string path, double width = double.NaN)
    {
        Measure(element, width);
        Write(Render(element, (int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight)), path);
    }

    /// <summary>Messen und anordnen, wie es ein Fenster tun würde.</summary>
    public static void Measure(FrameworkElement element, double width = double.NaN)
    {
        if (double.IsNaN(width) || width <= 0)
        {
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            element.Arrange(new Rect(0, 0, element.DesiredSize.Width, element.DesiredSize.Height));
        }
        else
        {
            // Feste Breite: so breit ist das Fenster wirklich.
            element.Measure(new Size(width, double.PositiveInfinity));
            element.Arrange(new Rect(0, 0, width, element.DesiredSize.Height));
        }

        element.UpdateLayout();
        Settle(element);
    }

    /// <summary>Farbe eines Pixels aus einem gerenderten Element (für Prüfungen).</summary>
    public static Color PixelAt(FrameworkElement element, double x, double y)
    {
        Settle(element);
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = Render(element, width, height);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        var px = (int)Math.Clamp(x, 0, width - 1);
        var py = (int)Math.Clamp(y, 0, height - 1);
        var index = ((py * width) + px) * 4;
        return Color.FromArgb(pixels[index + 3], pixels[index + 2], pixels[index + 1], pixels[index]);
    }

    private static RenderTargetBitmap Render(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static void Write(RenderTargetBitmap bitmap, string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
