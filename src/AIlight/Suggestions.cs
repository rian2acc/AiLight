using AIlight.Groq;

namespace AIlight;

public enum SuggestionKind
{
    /// <summary>Frage absenden (Enter).</summary>
    Ask,

    /// <summary>Befehl ausführen.</summary>
    Command,

    /// <summary>Frühere Frage übernehmen.</summary>
    History,
}

/// <summary>Eine Zeile in der Vorschlagsliste.</summary>
public sealed record Suggestion(string Glyph, string Title, string Subtitle, SuggestionKind Kind, string Payload = "")
{
    public static Suggestion ForCommand(CommandInfo command)
        => new("/", command.Display, command.Summary, SuggestionKind.Command, command.Name);

    public static Suggestion ForQuestion(string question, string modelLabel)
    {
        var title = question.Length > 70 ? question[..70] + "…" : question;
        return new Suggestion("✦", $"Fragen: „{title}“", modelLabel, SuggestionKind.Ask, question);
    }

    public static Suggestion ForHistory(HistoryEntry entry)
        => new("↺", entry.Title, entry.WhenText + " · " + ShortModel(entry.Model), SuggestionKind.History, entry.Id);

    private static string ShortModel(string model)
        => model.Contains("20b", StringComparison.OrdinalIgnoreCase) ? "20b"
            : model.Contains("120b", StringComparison.OrdinalIgnoreCase) ? "120b"
            : model;
}

/// <summary>Baut die Zeilen, die unter dem Eingabefeld erscheinen.</summary>
public static class SuggestionBuilder
{
    /// <summary>Wird geliefert, wenn die Liste nur noch aus Eingabehinweisen besteht.</summary>
    public static IReadOnlyList<Suggestion> Build(
        string query,
        HistoryStore history,
        AppSettings settings,
        bool hasAnswer)
    {
        var rows = new List<Suggestion>();
        var limit = Math.Max(0, settings.MaxSuggestions);

        if (Commands.IsCommandPrefix(query))
        {
            foreach (var command in Commands.Matching(query).Take(Math.Max(4, limit)))
            {
                rows.Add(Suggestion.ForCommand(command));
            }

            return rows;
        }

        if (query.Trim().Length > 0)
        {
            var (model, reason) = GroqModels.Resolve(settings.Model, query, settings.WebSearch);
            var label = model.Id[(model.Id.LastIndexOf('/') + 1)..] + " · " + reason;
            rows.Add(Suggestion.ForQuestion(query.Trim(), label));
            return rows;
        }

        // Leeres Feld: frühere Fragen zuerst, danach die wichtigsten Befehle.
        var recent = history.Recent(Math.Max(0, limit - 2));
        foreach (var entry in recent)
        {
            rows.Add(Suggestion.ForHistory(entry));
        }

        // Ohne Frage sind die wichtigsten Befehle der Startpunkt — mit Antwort
        // steht „kopieren“ vorn, sonst „neues Gespräch“.
        var shortcuts = new[]
        {
            hasAnswer ? "copy" : "new",
            "history",
            "settings",
            "help",
        };
        foreach (var name in shortcuts)
        {
            var command = Commands.All.FirstOrDefault(c => c.Name == name);
            if (command is not null && rows.Count < limit + 3)
            {
                rows.Add(Suggestion.ForCommand(command));
            }
        }

        return rows.Take(Math.Max(4, limit + 2)).ToList();
    }
}
