using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using AIlight.Core;

namespace AIlight.Chat;

/// <summary>Ein gespeicherter Austausch.</summary>
public sealed record HistoryEntry(
    string Id,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(FlexTimeConverter))] DateTimeOffset At,
    string Question,
    string Answer,
    string Model,
    int TokensIn,
    int TokensOut,
    double CostUsd)
{
    public string WhenText => At.LocalDateTime.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("de-DE"));

    /// <summary>Kurze Fassung der Frage für die Liste (caputt Dateien sind möglich).</summary>
    public string Title
    {
        get
        {
            var text = (Question ?? "").Trim();
            if (text.Length == 0)
            {
                return "(ohne Frage)";
            }

            return text.Length > 90 ? text[..90] + "…" : text;
        }
    }
}

/// <summary>
/// Verlauf als JSONL: eine Zeile je Antwort, wird angehängt, nie umgeschrieben.
/// Gefunden wird ein Eintrag über seine kurze Kennung (nicht über die Zeit —
/// zwei Antworten können dieselbe Uhrzeit tragen).
/// </summary>
public sealed class HistoryStore
{
    // Groß-/Kleinschreibung egal: so werden auch Verlaufsdateien früherer
    // Fassungen gelesen (dort hießen die Felder klein geschrieben).
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public HistoryStore(string path) => Path_ = path;

    public string Path_ { get; }

    public void Add(HistoryEntry entry)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path_)!);
            File.AppendAllText(Path_, JsonSerializer.Serialize(entry, Json) + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Log.Error("Verlauf konnte nicht geschrieben werden", ex);
        }
    }

    public IReadOnlyList<HistoryEntry> All()
    {
        if (!File.Exists(Path_))
        {
            return Array.Empty<HistoryEntry>();
        }

        var entries = new List<HistoryEntry>();
        try
        {
            foreach (var line in File.ReadLines(Path_, Encoding.UTF8))
            {
                if (line.Trim().Length == 0)
                {
                    continue;
                }

                try
                {
                    if (JsonSerializer.Deserialize<HistoryEntry>(line, Json) is { } entry)
                    {
                        entries.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    // Eine kaputte Zeile darf den Rest nicht unbrauchbar machen.
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Verlauf konnte nicht gelesen werden", ex);
        }

        return entries;
    }

    /// <summary>Die neuesten Einträge, neueste zuerst.</summary>
    public IReadOnlyList<HistoryEntry> Recent(int limit)
    {
        var all = All();
        var ordered = all.OrderByDescending(e => e.At).ToList();
        return limit <= 0 ? ordered : ordered.Take(limit).ToList();
    }

    public IReadOnlyList<HistoryEntry> Search(string term, int limit)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return Recent(limit);
        }

        var needle = term.Trim();
        var found = All()
            .Where(e => e.Question.Contains(needle, StringComparison.OrdinalIgnoreCase)
                        || e.Answer.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.At)
            .ToList();
        return limit <= 0 ? found : found.Take(limit).ToList();
    }

    public HistoryEntry? ById(string id) => All().LastOrDefault(e => e.Id == id);

    /// <summary>Einträge löschen (leeres Wort = alle). Gibt die Anzahl zurück.</summary>
    public int Delete(string term)
    {
        var all = All();
        var keep = string.IsNullOrWhiteSpace(term)
            ? new List<HistoryEntry>()
            : all.Where(e => !e.Question.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase)
                             && !e.Answer.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        var removed = all.Count - keep.Count;
        Write(keep);
        return removed;
    }

    public int DeleteById(string id)
    {
        var all = All();
        var keep = all.Where(e => e.Id != id).ToList();
        Write(keep);
        return all.Count - keep.Count;
    }

    /// <summary>Alte Einträge entfernen (<paramref name="keepDays"/> 0 = alles behalten).</summary>
    public int Prune(int keepDays, int keepEntries = 0)
    {
        var all = All();
        var keep = keepDays > 0
            ? all.Where(e => e.At >= DateTimeOffset.Now.AddDays(-keepDays)).ToList()
            : all.ToList();

        // Zusätzlich die Zahl begrenzen: sonst wächst die Datei allein durch
        // viele Fragen an einem Tag weiter, ohne dass die Aufbewahrung greift.
        if (keepEntries > 0 && keep.Count > keepEntries)
        {
            keep = keep.TakeLast(keepEntries).ToList();
        }

        if (keep.Count == all.Count)
        {
            return 0;
        }

        Write(keep);
        return all.Count - keep.Count;
    }

    private void Write(IReadOnlyList<HistoryEntry> entries)
    {
        try
        {
            var text = string.Concat(entries.Select(e => JsonSerializer.Serialize(e, Json) + Environment.NewLine));
            AtomicFile.WriteAllText(Path_, text, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Log.Error("Verlauf konnte nicht geschrieben werden", ex);
        }
    }

    public (int Count, int TokensIn, int TokensOut, double Cost, long Bytes) Stats()
    {
        var all = All();
        var size = File.Exists(Path_) ? new FileInfo(Path_).Length : 0;
        return (
            all.Count,
            all.Sum(e => e.TokensIn),
            all.Sum(e => e.TokensOut),
            all.Sum(e => e.CostUsd),
            size);
    }

    /// <summary>Kennung für einen neuen Eintrag (kurz, eindeutig, sortierbar).</summary>
    public static string NewId()
        => DateTime.Now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture)
           + "-" + Random.Shared.Next(0x100, 0xFFF).ToString("x", CultureInfo.InvariantCulture);

    /// <summary>Die zuletzt gespeicherte Antwort (für „Antwort speichern“ und das Fenster).</summary>
    public HistoryEntry? Latest() => Recent(1).FirstOrDefault();
}
