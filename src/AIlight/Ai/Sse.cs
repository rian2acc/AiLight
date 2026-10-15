using System.Runtime.CompilerServices;
using System.Text;

namespace AIlight.Ai;

/// <summary>Ein Rahmen aus einem Ereignisstrom: der Name (wenn es einen gibt) und die Daten.</summary>
public readonly record struct SseFrame(string Event, string Data);

/// <summary>
/// Liest Ereignisströme (<c>text/event-stream</c>), wie sie OpenAI, Anthropic und
/// Google schicken. Alle drei benutzen dieselbe Form — nur unterschiedlich streng:
///
/// <list type="bullet">
///   <item>OpenAI-kompatible Anbieter schicken nur <c>data:</c>-Zeilen und am Ende
///   <c>data: [DONE]</c>.</item>
///   <item>Anthropic stellt jedem Rahmen ein <c>event:</c> voran.</item>
///   <item>Google schickt JSON-Rahmen mit <c>data:</c>; Kommentarzeilen
///   (beginnend mit <c>:</c>) kommen bei OpenRouter vor und werden übersprungen.</item>
/// </list>
///
/// Mehrere <c>data:</c>-Zeilen hintereinander gehören zu einem Rahmen und werden
/// mit Zeilenumbruch zusammengefügt — das ist die Regel des Formats, nicht Zierde.
/// </summary>
public static class Sse
{
    /// <summary>Das Ende, das die OpenAI-Form schickt.</summary>
    public const string Done = "[DONE]";

    public static async IAsyncEnumerable<SseFrame> ReadAsync(
        TextReader reader,
        [EnumeratorCancellation] CancellationToken token)
    {
        var name = "";
        var data = new StringBuilder();

        while (!token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    yield return new SseFrame(name, data.ToString());
                    name = "";
                    data.Clear();
                }

                continue;
            }

            if (line[0] == ':')
            {
                // Ein Kommentar: manche Dienste halten damit die Verbindung offen.
                continue;
            }

            var colon = line.IndexOf(':');
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? "" : line[(colon + 1)..].TrimStart();

            switch (field)
            {
                case "event":
                    name = value;
                    break;
                case "data":
                    if (data.Length > 0)
                    {
                        data.Append('\n');
                    }

                    data.Append(value);
                    break;
                default:
                    // id:, retry: und alles andere brauchen wir nicht.
                    break;
            }
        }

        if (data.Length > 0)
        {
            yield return new SseFrame(name, data.ToString());
        }
    }
}
