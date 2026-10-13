namespace AIlight;

/// <summary>Ein Befehl der Leiste. „/“ ist die neue Form, „#“ bleibt gültig.</summary>
public sealed record CommandInfo(string Name, string Summary, string ArgumentHint = "")
{
    public string Display => ArgumentHint.Length > 0 ? $"/{Name} {ArgumentHint}" : $"/{Name}";
}

/// <summary>Alle Befehle an einer Stelle — die Hilfe zeigt genau diese Liste.</summary>
public static class Commands
{
    public static IReadOnlyList<CommandInfo> All { get; } = new[]
    {
        new CommandInfo("new", "Neues Gespräch beginnen (Gedächtnis leeren)"),
        new CommandInfo("clip", "Frage zur Zwischenablage stellen"),
        new CommandInfo("history", "Verlauf durchsuchen und öffnen", "[Wort]"),
        new CommandInfo("forget", "Verlauf löschen (ohne Wort: alles)", "[Wort]"),
        new CommandInfo("stats", "Nutzung: Anzahl, Tokens, Kosten"),
        new CommandInfo("model", "Modell wählen: auto, 20b, 120b", "[auto|20b|120b]"),
        new CommandInfo("prompt", "System-Prompt anzeigen"),
        new CommandInfo("conversation", "Gespräch anzeigen, das mitgesendet wird"),
        new CommandInfo("copy", "Letzte Antwort in die Zwischenablage kopieren"),
        new CommandInfo("save", "Letzte Antwort als Datei speichern"),
        new CommandInfo("settings", "Einstellungen öffnen"),
        new CommandInfo("help", "Diese Befehlsliste anzeigen"),
        new CommandInfo("quit", "AIlight beenden"),
    };

    /// <summary>Beginnt der Text mit einem Befehlszeichen?</summary>
    public static bool IsCommandPrefix(string text)
        => text.StartsWith('/') || text.StartsWith('#');

    /// <summary>Zerlegt „/history Urlaub“ in Befehl und Rest. Unbekanntes → <c>null</c>.</summary>
    public static (CommandInfo Command, string Argument)? Split(string text)
    {
        var trimmed = (text ?? "").Trim();
        if (!IsCommandPrefix(trimmed))
        {
            return null;
        }

        var body = trimmed[1..];
        var space = body.IndexOf(' ');
        var name = (space < 0 ? body : body[..space]).Trim().ToLowerInvariant();
        var argument = space < 0 ? "" : body[(space + 1)..].Trim();
        var command = All.FirstOrDefault(c => c.Name == name);
        return command is null ? null : (command, argument);
    }

    /// <summary>Vorschläge, wenn der Nutzer gerade einen Befehl tippt.</summary>
    public static IReadOnlyList<CommandInfo> Matching(string text)
    {
        var body = (text ?? "").Trim().TrimStart('/', '#').ToLowerInvariant();
        return All
            .Where(c => c.Name.StartsWith(body, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
