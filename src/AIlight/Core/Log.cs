using System.IO;
using System.Text;

namespace AIlight.Core;

/// <summary>
/// Ein einziges, rotierendes Logfile. Es enthält nur Ereignisse und Fehler —
/// niemals Fragen, Antworten oder den API-Schlüssel (Datenschutz).
/// </summary>
public static class Log
{
    private const long MaxBytes = 1 * 1024 * 1024; // 1 MB je Datei
    private const int MaxFiles = 4;
    private static readonly object Gate = new();
    private static string _path = "";
    private static bool _enabled = true;

    public static void Start(string folder, bool enabled)
    {
        _enabled = enabled;
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "ailight.log");
    }

    public static string Path_ => _path;

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? exception = null)
    {
        if (exception is null)
        {
            Write("ERROR", message);
            return;
        }

        // Mit Aufrufliste, sonst ist ein Fehlerbericht wertlos. Gekürzt, damit
        // die Logdatei klein bleibt.
        var detail = exception.ToString();
        if (detail.Length > 1500)
        {
            detail = detail[..1500] + " …";
        }

        Write("ERROR", $"{message} — {detail}");
    }

    private static void Write(string level, string message)
    {
        if (!_enabled || _path.Length == 0)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                Rotate();
                File.AppendAllText(
                    _path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Ein Logfehler darf das Programm nie stören.
        }
    }

    private static void Rotate()
    {
        var file = new FileInfo(_path);
        if (!file.Exists || file.Length < MaxBytes)
        {
            return;
        }

        for (var index = MaxFiles - 1; index >= 1; index--)
        {
            var older = $"{_path}.{index}";
            var newer = $"{_path}.{index + 1}";
            if (File.Exists(newer))
            {
                File.Delete(newer);
            }

            if (File.Exists(older))
            {
                File.Move(older, newer);
            }
        }

        File.Move(_path, _path + ".1", overwrite: true);
    }
}
