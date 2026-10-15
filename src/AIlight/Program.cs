using System.Runtime.InteropServices;
using System.Windows;
using AIlight.Core;

namespace AIlight;

/// <summary>
/// Einstiegspunkt. Er entscheidet vor allem anderen, ob dieses Programm nur
/// eine Nachricht an eine laufende Instanz schicken soll oder selbst startet.
/// </summary>
internal static class Program
{
    /// <summary>An die Konsole des aufrufenden Fensters anhängen (Start aus einem Terminal).</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [STAThread]
    private static int Main(string[] args)
    {
        var options = Options.Parse(args);

        // Ein Fensterprogramm hat keine eigene Konsole: für die Prüf- und Messmodi
        // hängen wir uns an das Terminal, aus dem der Aufruf kam.
        if (options.SelfTest || options.Report.Length > 0 || options.Quit)
        {
            AttachConsole(-1);
        }

        if (options.SelfTest)
        {
            return SelfTest.RunAll();
        }

        // Messmodi gehören nicht der laufenden Sitzung: Wer Bildschirmfotos machen
        // oder einen Messlauf starten will, soll das auch dann können, wenn im
        // Infobereich bereits eine Fassung wartet. (Beide würden sich sonst nur
        // gegenseitig benachrichten, und der Messlauf stürbe wortlos.)
        if (options.Shot.Length > 0 || options.SmokeSeconds > 0 || options.AskUi.Length > 0 || options.Ask.Length > 0)
        {
            var measuring = new App(options);
            return measuring.Run();
        }

        using var single = new SingleInstance();
        if (!single.IsFirst)
        {
            var sent = SingleInstance.Send(options.Quit ? "quit" : "show");
            Console.WriteLine(sent
                ? "ailight: laufende Instanz benachrichtigt"
                : "ailight: keine laufende Instanz gefunden");
            return sent || options.Quit ? 0 : 1;
        }

        if (options.Quit)
        {
            Console.WriteLine("ailight: es läuft keine Instanz");
            return 0;
        }

        var app = new App(options);
        app.AttachSingleInstance(single);
        return app.Run();
    }
}
