using System.IO;
using System.Text;

namespace AIlight.Core;

/// <summary>
/// Schreibt Dateien atomar: erst die Kopie neben der Zieldatei, dann umbenennen.
/// Bricht der Schreibvorgang ab (Absturz, voller Datenträger), bleibt die alte
/// Datei unversehrt — Verlauf, Gespräch und Einstellungen können nicht
/// halbgeschrieben ankommen.
/// </summary>
public static class AtomicFile
{
    /// <summary>UTF-8 ohne BOM — so schreibt <c>File.WriteAllText</c> auch ohne Codierung.</summary>
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string text, Encoding? encoding = null)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text, encoding ?? Utf8NoBom);
        File.Move(temp, path, overwrite: true);
    }
}
