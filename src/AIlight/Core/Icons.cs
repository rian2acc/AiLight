using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Brush = System.Drawing.Brush;
using Color = System.Drawing.Color;
using Size = System.Drawing.Size;

namespace AIlight.Core;

/// <summary>
/// Das Programmsymbol. Bevorzugt wird die mitgelieferte Datei
/// <c>ailight.ico</c>; fehlt sie, wird ein einfaches Zeichen gezeichnet —
/// so sieht das Programm nie „kapitutt“ aus.
/// </summary>
public static class Icons
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon Load(int size = 32)
    {
        var path = Paths.IconFile;
        if (path.Length > 0)
        {
            try
            {
                return new Icon(path, new Size(size, size));
            }
            catch (Exception ex)
            {
                Log.Warn($"Symbol konnte nicht geladen werden: {ex.Message}");
            }
        }

        // Keine Datei daneben: das im Programm eingebettete Symbol benutzen —
        // dann sieht auch das Infobereich-Symbol überall gleich aus.
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                if (Icon.ExtractAssociatedIcon(exe) is { } embedded)
                {
                    using (embedded)
                    {
                        return new Icon(embedded, new Size(size, size));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Eingebettetes Symbol nicht nutzbar: {ex.Message}");
        }

        return Draw(size);
    }

    /// <summary>
    /// Notfallsymbol, falls die Datei fehlt: dieselbe Marke wie im Programmsymbol —
    /// abgerundete Fläche, darin der Lichtpunkt mit den drei Strahlen, die
    /// zusammen „AI“ ergeben.
    /// </summary>
    public static Icon Draw(int size, string accent = Views.Theme.DefaultAccent)
    {
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            var color = Views.Theme.Parse(accent) is { } parsed
                ? Color.FromArgb(parsed.R, parsed.G, parsed.B)
                : Color.FromArgb(0x0A, 0x84, 0xFF);

            // Fläche mit sanftem Verlauf (oben heller) — wie im gezeichneten Symbol.
            var inset = size * 0.055f;
            using var face = RoundedRectangle(inset, inset, size - (2 * inset), size - (2 * inset), size * 0.23f);
            using (var gradient = new System.Drawing.Drawing2D.LinearGradientBrush(
                       new RectangleF(0, 0, size, size), Lighten(color, 0.35f), color, 90f))
            {
                graphics.FillPath(gradient, face);
            }

            // Alles Licht bleibt innerhalb der Fläche — sonst franst der Schein am
            // Rand des Symbols aus und wird hart abgeschnitten.
            var clip = graphics.Clip;
            graphics.SetClip(face);

            // Die Marke: Lichtpunkt oben, daraus gehen drei Strahlen nach unten.
            // Die beiden äußeren Strahlen mit dem Querstrich dazwischen bilden
            // ein A, der mittlere Strahl ist das I. Dieselben Maße wie in
            // tools/make_icon.py und Views/Theme.xaml (Raster 100 × 100).
            var small = size <= 24;
            var box = size * (small ? 0.70f : 0.64f);
            var origin = (size - box) / 2f;
            var stroke = box * 0.11f * (small ? 1.12f : 1f);

            PointF At(float x, float y)
                => new(origin + (x / 100f * box), origin + (y / 100f * box));

            using (var pen = new System.Drawing.Pen(Color.FromArgb(245, 255, 255, 255), stroke))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                graphics.DrawLine(pen, At(50, 32), At(50, 90));   // das I
                graphics.DrawLine(pen, At(44, 32), At(22, 90));   // linker Schenkel
                graphics.DrawLine(pen, At(56, 32), At(78, 90));   // rechter Schenkel
                graphics.DrawLine(pen, At(30, 68), At(70, 68));   // Querstrich des A
            }

            // Der Lichtpunkt (die Quelle): weicher Hof, dann der helle Kern.
            var point = At(50, 14);
            var pointRadius = box * 0.095f;

            if (!small)
            {
                using var halo = new SolidBrush(Color.FromArgb(110, 255, 255, 255));
                var haloRadius = pointRadius * 2.4f;
                graphics.FillEllipse(halo,
                    point.X - haloRadius, point.Y - haloRadius,
                    haloRadius * 2, haloRadius * 2);
            }

            using (var core = new SolidBrush(Color.White))
            {
                graphics.FillEllipse(core,
                    point.X - pointRadius, point.Y - pointRadius,
                    pointRadius * 2, pointRadius * 2);
            }

            graphics.Clip = clip;
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    /// <summary>Eine Farbe Richtung Weiß aufhellen (für den Verlauf der Fläche).</summary>
    private static Color Lighten(Color color, float amount) => Color.FromArgb(
        color.R + (int)((255 - color.R) * amount),
        color.G + (int)((255 - color.G) * amount),
        color.B + (int)((255 - color.B) * amount));

    /// <summary>Ein abgerundetes Rechteck als Pfad (Fläche und Beschnitt).</summary>
    private static GraphicsPath RoundedRectangle(float x, float y, float width, float height, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
