namespace AIlight.Groq;

/// <summary>Ein Modell mit Anzeigenamen und Preis (USD je 1 Mio. Tokens).</summary>
public sealed record ModelInfo(string Id, string Label, double InputPerMillion, double OutputPerMillion)
{
    /// <summary>Preis für einen Aufruf in US-Dollar.</summary>
    public double Cost(int inputTokens, int outputTokens)
        => (inputTokens / 1_000_000.0 * InputPerMillion) + (outputTokens / 1_000_000.0 * OutputPerMillion);
}

/// <summary>
/// Die Modelle, die AIlight anbietet. Beide unterstützen Denk-Schritte („Reasoning“)
/// und die eingebaute Websuche von Groq — deshalb reichen zwei Einträge.
/// </summary>
public static class GroqModels
{
    public const string Auto = "auto";

    public static readonly ModelInfo Small =
        new("openai/gpt-oss-20b", "gpt-oss-20b — schnell und günstig", 0.075, 0.30);

    public static readonly ModelInfo Large =
        new("openai/gpt-oss-120b", "gpt-oss-120b — stärker für schwere Fragen", 0.15, 0.60);

    public static IReadOnlyList<ModelInfo> All { get; } = new[] { Small, Large };

    public static ModelInfo ById(string id) => id == Large.Id ? Large : Small;

    /// <summary>
    /// Modell wählen: „auto“ entscheidet nach der Frage — Websuche und lange
    /// Fragen brauchen das große Modell, kurze Fragen das schnelle.
    /// </summary>
    public static (ModelInfo Model, string Reason) Resolve(string setting, string question, bool webSearch)
    {
        if (!string.IsNullOrWhiteSpace(setting) && setting != Auto)
        {
            return (ById(setting), "im Einstellungsfenster gewählt");
        }

        if (webSearch)
        {
            return (Large, "Websuche braucht das große Modell");
        }

        return question.Length > 240 || question.Contains("```")
            ? (Large, "längere Frage")
            : (Small, "kurze Frage (schnell und günstig)");
    }

    /// <summary>Anzeigetext für die Fußzeile, z. B. „~$0,0003“.</summary>
    public static string FormatCost(double usd)
    {
        if (usd <= 0)
        {
            return "~$0";
        }

        if (usd < 0.0001)
        {
            return "unter $0,0001";
        }

        return "~$" + usd.ToString("0.0000", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
    }

    /// <summary>Grober Token-Schätzer, wenn die API keine Nutzung mitschickt (4 Zeichen ≈ 1 Token).</summary>
    public static int EstimateTokens(int characters) => Math.Max(1, characters / 4);
}
