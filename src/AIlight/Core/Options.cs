namespace AIlight.Core;

/// <summary>
/// Kommandozeile. Die Prüf- und Messmodi sind klein gehalten, aber ehrlich:
/// sie messen dieselbe EXE, die auch der Nutzer startet.
/// </summary>
public sealed class Options
{
    public bool SelfTest { get; private set; }
    public bool Quit { get; private set; }

    /// <summary>Die Einführung zeigen, egal ob sie schon gesehen wurde.</summary>
    public bool Welcome { get; private set; }

    /// <summary>Den Rundgang über die Leiste zeigen (die Leiste erscheint mit).</summary>
    public bool Tour { get; private set; }
    public bool TraceStart { get; private set; }
    public double SmokeSeconds { get; private set; }
    public string Ask { get; private set; } = "";
    public string Report { get; private set; } = "";
    public bool ShowBar { get; private set; }

    /// <summary>Frage, die wie von Hand in die Leiste getippt und abgeschickt wird.</summary>
    public string AskUi { get; private set; } = "";

    /// <summary>Ordner für Bildschirmfotos der Fenster (leer = keine).</summary>
    public string Shot { get; private set; } = "";

    /// <summary>Kurzbeschreibung für das Log (welcher Modus läuft).</summary>
    public string Summary => SelfTest ? "Selbsttest"
        : Quit ? "Beenden"
        : Welcome ? "Einführung"
        : Tour ? "Rundgang"
        : Shot.Length > 0 ? "Bildschirmfotos"
        : AskUi.Length > 0 ? "Frage über die Leiste"
        : Ask.Length > 0 ? "Einzelfrage"
        : SmokeSeconds > 0 ? "Messlauf"
        : "normaler Start";

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--selftest":
                    options.SelfTest = true;
                    break;
                case "--quit":
                    options.Quit = true;
                    break;
                case "--welcome":
                    options.Welcome = true;
                    break;
                case "--tour":
                    options.Tour = true;
                    break;
                case "--trace-start":
                    options.TraceStart = true;
                    break;
                case "--show":
                    options.ShowBar = true;
                    break;
                case "--smoke":
                    options.SmokeSeconds = NextDouble(args, ref index, 3.0);
                    break;
                case "--ask":
                    options.Ask = Next(args, ref index, "");
                    options.SmokeSeconds = NextDouble(args, ref index, 45.0);
                    break;
                case "--ask-ui":
                    options.AskUi = Next(args, ref index, "");
                    break;
                case "--shot":
                    options.Shot = Next(args, ref index, "");
                    break;
                case "--report":
                    options.Report = Next(args, ref index, "");
                    break;
                default:
                    break;
            }
        }

        return options;
    }

    private static string Next(string[] args, ref int index, string fallback)
    {
        if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            index++;
            return args[index];
        }

        return fallback;
    }

    private static double NextDouble(string[] args, ref int index, double fallback)
    {
        var text = Next(args, ref index, "");
        return double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }
}
