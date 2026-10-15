using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AIlight.Core;

namespace AIlight.Ai;

/// <summary>Eine Nachricht im Gespräch.</summary>
public sealed record ChatMessage(string Role, string Content);

/// <summary>Eine Frage — fertig zusammengesetzt, unabhängig vom Anbieter.</summary>
public sealed class AskRequest
{
    public required string Model { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public int MaxCompletionTokens { get; init; } = 2048;
    public string? ReasoningEffort { get; init; }
    public bool WebSearch { get; init; }
    public double Temperature { get; init; } = 0.7;

    /// <summary>Nur senden, wenn < 1 — manche Anbieter lehnen den Wert 1 nicht ab, andere schon.</summary>
    public double TopP { get; init; } = 1;

    /// <summary>Erweiterte Denk-Schritte ausdrücklich anfordern (Anthropic/Gemini).</summary>
    public bool Thinking { get; init; }

    /// <summary>Als Strom (Stück für Stück) oder als eine Antwort am Ende?</summary>
    public bool Stream { get; init; } = true;
}

/// <summary>Ein Aufruf: Anbieter, Adresse, Schlüssel und Frage zusammen.</summary>
public sealed record AiCall(
    ProviderInfo Provider,
    string BaseUrl,
    string ApiKey,
    AskRequest Request,
    Action<string>? OnText = null,
    Action<string>? OnReasoning = null,
    int Attempts = 3,
    TimeSpan? Timeout = null)
{
    public TimeSpan EffectiveTimeout => Timeout ?? TimeSpan.FromSeconds(120);

    public string Address => BaseUrl.TrimEnd('/');
}

/// <summary>Ergebnis einer Antwort (auch wenn sie abgebrochen wurde).</summary>
public sealed record AskResult(
    string Text,
    string Reasoning,
    string Model,
    int InputTokens,
    int OutputTokens,
    double CostUsd,
    TimeSpan Duration,
    string FinishReason,
    bool Cancelled,
    bool Truncated);

/// <summary>
/// Sammelt, was aus dem Strom hereinkommt: Text, Denk-Schritte, Nutzung und
/// den Grund für das Ende. Jeder Anbieter füllt dieselbe Schale.
/// </summary>
public sealed class AiStream
{
    public StringBuilder Text { get; } = new();
    public StringBuilder Reasoning { get; } = new();
    public string FinishReason { get; set; } = "";

    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }

    /// <summary>Wahr, sobald auch nur ein Stück an die Oberfläche ging.</summary>
    public bool Delivered { get; private set; }

    /// <summary>
    /// Der Trenner behält seinen Zustand: eine Marke kann genau zwischen zwei
    /// Stücken liegen („&lt;thi“ kommt an, „nk&gt;“ folgt). Ohne diesen Zustand
    /// landeten Teile der Marke in der Antwort.
    /// </summary>
    private ThinkSplitter? _split;

    /// <summary>Text anhängen. <c>&lt;think&gt;…&lt;/think&gt;</c> im Text gilt als Denk-Schritt.</summary>
    public void AddText(string piece, Action<string>? onText)
    {
        if (piece.Length == 0)
        {
            return;
        }

        _split ??= new ThinkSplitter(Reasoning);
        foreach (var part in _split.Push(piece))
        {
            if (part.Length == 0)
            {
                continue;
            }

            Text.Append(part);
            Delivered = true;
            onText?.Invoke(part);
        }
    }

    /// <summary>Denk-Schritte anhängen — sie sind nie Teil der Antwort.</summary>
    public void AddReasoning(string piece, Action<string>? onReasoning)
    {
        if (piece.Length == 0)
        {
            return;
        }

        Reasoning.Append(piece);
        Delivered = true;
        onReasoning?.Invoke(piece);
    }

    public void SetUsage(int input, int output)
    {
        if (input > 0)
        {
            InputTokens = input;
        }

        if (output > 0)
        {
            OutputTokens = output;
        }
    }
}

/// <summary>
/// Die gemeinsame Grundlage aller Anbieterklienten: eine HTTP-Verbindung für alle,
/// Wiederholung bei 429/5xx, lesbare Fehlersätze und dieselbe Rückgabe.
/// </summary>
public abstract class AiClient
{
    protected static readonly HttpClient Http = CreateHttp();

    /// <summary>Den Anfragetext bauen (JSON) — auch für den Prüfstand sichtbar.</summary>
    public abstract string Payload(AiCall call);

    /// <summary>Den fertigen HTTP-Aufruf bauen (Adresse, Kopfzeilen, Text).</summary>
    protected abstract HttpRequestMessage Build(AiCall call);

    /// <summary>Einen Rahmen aus dem Ereignisstrom verarbeiten.</summary>
    protected abstract void Handle(SseFrame frame, AiStream stream, AiCall call);

    /// <summary>Eine vollständige Antwort (ohne Strom) verarbeiten.</summary>
    protected abstract void ParseAnswer(string json, AiStream stream, AiCall call);

    /// <summary>Wie die Antwort heißt, wenn das Ende erreicht ist (z. B. „length“).</summary>
    protected virtual bool WasTruncated(string finishReason)
        => finishReason is "length" or "max_tokens" or "MAX_TOKENS";

    public async Task<AskResult> StreamAsync(AiCall call, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var stream = new AiStream();
        var attempt = 0;

        while (true)
        {
            attempt++;
            token.ThrowIfCancellationRequested();
            using var clock = CancellationTokenSource.CreateLinkedTokenSource(token);
            clock.CancelAfter(call.EffectiveTimeout);

            try
            {
                using var message = Build(call);
                using var response = await Http
                    .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, clock.Token)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(clock.Token).ConfigureAwait(false);

                    // Zwei Dinge getrennt halten: ob ein neuer Versuch überhaupt helfen
                    // kann (429, 5xx) und ob jetzt noch einer übrig ist. Ein abgelehnter
                    // Schlüssel (401) ist nie „wiederholbar“ — ein Hinweis „Enter fragt
                    // erneut“ führte dort nur in die Irre.
                    var retryable = Retryable(response.StatusCode);
                    var wait = retryable ? RetryDelay(response, attempt, call.Attempts) : null;
                    if (wait is { } delay && !stream.Delivered)
                    {
                        Log.Warn($"{call.Provider.Name} antwortete {(int)response.StatusCode} — "
                            + $"neuer Versuch in {delay.TotalSeconds:0.0} s");
                        await Task.Delay(delay, token).ConfigureAwait(false);
                        continue;
                    }

                    throw new AiException(Describe(call.Provider, response.StatusCode, body), retryable);
                }

                await using var content = await response.Content.ReadAsStreamAsync(clock.Token).ConfigureAwait(false);
                using var reader = new System.IO.StreamReader(content, Encoding.UTF8);

                if (call.Request.Stream)
                {
                    await foreach (var frame in Sse.ReadAsync(reader, clock.Token).ConfigureAwait(false))
                    {
                        if (frame.Data.Length == 0)
                        {
                            continue;
                        }

                        if (frame.Data == Sse.Done)
                        {
                            break;
                        }

                        Handle(frame, stream, call);
                    }
                }
                else
                {
                    ParseAnswer(await reader.ReadToEndAsync(clock.Token).ConfigureAwait(false), stream, call);
                }

                watch.Stop();

                // Wurde zwischendurch abgebrochen (der Strom endet dann ohne Fehler),
                // ist das Ergebnis ehrlich als abgebrochen zu melden.
                return Finish(call, stream, watch, cancelled: token.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                watch.Stop();
                throw new AiException(
                    $"{call.Provider.Name} hat nicht innerhalb von {call.EffectiveTimeout.TotalSeconds:0} s "
                    + "geantwortet. In den Einstellungen lässt sich die Wartezeit verlängern.",
                    retryable: true);
            }
            catch (OperationCanceledException)
            {
                watch.Stop();
                return Finish(call, stream, watch, cancelled: true);
            }
            catch (HttpRequestException ex)
            {
                if (stream.Delivered)
                {
                    watch.Stop();
                    throw new AiException(
                        $"Die Verbindung zu {call.Provider.Name} ist mitten in der Antwort abgerissen — "
                        + "der bisherige Text bleibt stehen. Enter fragt erneut.",
                        retryable: true, ex);
                }

                if (attempt < Math.Max(1, call.Attempts))
                {
                    var delay = TimeSpan.FromSeconds(Math.Min(10, 1.5 * attempt));
                    Log.Warn($"Netzfehler ({ex.Message}) — neuer Versuch in {delay.TotalSeconds:0.0} s");
                    await Task.Delay(delay, token).ConfigureAwait(false);
                    continue;
                }

                throw new AiException(
                    $"Keine Verbindung zu {call.Provider.Name}. Bitte Internetverbindung prüfen.",
                    retryable: true, ex);
            }
        }
    }

    /// <summary>Ganze Antwort: Text, Denk-Schritte, Nutzung und Ende zusammenrechnen.</summary>
    protected AskResult Finish(AiCall call, AiStream stream, Stopwatch watch, bool cancelled)
    {
        var model = call.Provider.Model(call.Request.Model);
        var text = stream.Text.ToString();
        var reasoning = stream.Reasoning.ToString();
        var input = stream.InputTokens > 0
            ? stream.InputTokens
            : Providers.EstimateTokens(call.Request.Messages.Sum(message => message.Content.Length) + 20);
        var output = stream.OutputTokens > 0
            ? stream.OutputTokens
            : Providers.EstimateTokens(text.Length + reasoning.Length);

        return new AskResult(
            text,
            reasoning,
            call.Request.Model,
            input,
            output,
            model.Priced ? model.Cost(input, output) : 0,
            watch.Elapsed,
            stream.FinishReason,
            cancelled,
            WasTruncated(stream.FinishReason));
    }

    /// <summary>
    /// Schlüssel prüfen: eine winzige echte Anfrage. Der zurückgegebene Satz ist
    /// der Satz, den die Oberfläche anzeigt.
    /// </summary>
    public async Task<string> TestKeyAsync(AiCall call, CancellationToken token)
    {
        var probe = call with
        {
            Request = new AskRequest
            {
                Model = call.Provider.Fast.Id,
                Messages = new[] { new ChatMessage("user", "Antworte mit genau einem Wort: OK") },
                MaxCompletionTokens = 24,
                Temperature = 0,
            },
        };

        var result = await StreamAsync(probe, token).ConfigureAwait(false);
        var answer = result.Text.Trim();
        var word = answer.Length > 40 ? answer[..40] + "…" : answer;
        return answer.Length == 0
            ? $"{call.Provider.Name}: Der Schlüssel funktioniert (Antwort ohne Text)."
            : $"{call.Provider.Name}: Der Schlüssel funktioniert — Antwort „{word}“ "
              + $"({result.Duration.TotalSeconds:0.0} s).";
    }

    /// <summary>Die Modelle abfragen, die der Anbieter (oder ein lokaler Server) anbietet.</summary>
    public async Task<IReadOnlyList<string>> ModelsAsync(AiCall call, CancellationToken token)
    {
        using var clock = CancellationTokenSource.CreateLinkedTokenSource(token);
        clock.CancelAfter(TimeSpan.FromSeconds(20));

        using var message = new HttpRequestMessage(HttpMethod.Get, call.Address + "/models");
        Authorize(message, call);

        using var response = await Http.SendAsync(message, clock.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AiException(
                $"{call.Provider.Name} hat die Modellliste abgelehnt ({(int)response.StatusCode}).",
                retryable: false);
        }

        var json = await response.Content.ReadAsStringAsync(clock.Token).ConfigureAwait(false);
        return Providers.ReadModelNames(json);
    }

    /// <summary>Wie der Schlüssel ankommt: fast überall „Bearer“, bei Azure „api-key“.</summary>
    protected virtual void Authorize(HttpRequestMessage message, AiCall call)
    {
        if (call.Provider.Id == "azure")
        {
            message.Headers.TryAddWithoutValidation("api-key", call.ApiKey);
            return;
        }

        if (call.ApiKey.Length > 0)
        {
            message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + call.ApiKey);
        }
    }

    /// <summary>Kann ein neuer Versuch bei diesem Status überhaupt helfen?</summary>
    protected static bool Retryable(HttpStatusCode status)
        => (int)status is 408 or 409 or 425 or 429 or 500 or 502 or 503 or 504 or 529 or 498;

    /// <summary>Nach welcher Zeit ein neuer Versuch sinnvoll ist (<c>null</c> = aufgeben).</summary>
    protected static TimeSpan? RetryDelay(HttpResponseMessage response, int attempt, int attempts)
    {
        if (!Retryable(response.StatusCode) || attempt >= Math.Max(1, attempts))
        {
            return null;
        }

        if (response.Headers.RetryAfter is { } header)
        {
            if (header.Delta is { } delta)
            {
                return delta > TimeSpan.FromSeconds(15) ? TimeSpan.FromSeconds(15) : delta;
            }

            if (header.Date is { } date)
            {
                var wait = date - DateTimeOffset.UtcNow;
                return wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(1.5);
            }
        }

        return TimeSpan.FromSeconds(Math.Min(10, 1.5 * attempt));
    }

    /// <summary>HTTP-Fehler in einen lesbaren deutschen Satz übersetzen.</summary>
    protected static string Describe(ProviderInfo provider, HttpStatusCode status, string body)
    {
        var detail = ReadErrorMessage(body);
        var suffix = detail.Length > 0 ? $" („{detail}“)" : "";
        var name = provider.Name;
        return (int)status switch
        {
            400 => $"{name} hat die Anfrage abgelehnt (400){suffix}",
            401 => $"Der Schlüssel wird von {name} abgelehnt (401){suffix} — bitte in den Einstellungen prüfen.",
            402 => $"{name} verlangt eine Zahlung oder ein Guthaben (402){suffix}",
            403 => $"Der Zugriff wurde von {name} verweigert (403){suffix}",
            404 => $"Das gewählte Modell ist bei {name} nicht verfügbar (404){suffix} — "
                   + "in den Einstellungen ein anderes Modell wählen oder den Namen eintippen.",
            413 => $"Die Anfrage ist zu groß (413){suffix} — bitte ein neues Gespräch beginnen.",
            422 => $"{name} konnte die Anfrage nicht verarbeiten (422){suffix}",
            429 => $"Zu viele Anfragen (429){suffix} — bitte kurz warten.",
            498 => $"{name} hat gerade keine freie Kapazität (498) — bitte später erneut versuchen.",
            >= 500 => $"{name} meldet einen Serverfehler ({(int)status}){suffix} — bitte erneut versuchen.",
            _ => $"{name} hat mit Fehler {(int)status} geantwortet{suffix}",
        };
    }

    private static string ReadErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            // Die drei Formen, die in freier Wildbahn vorkommen.
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return Short(error.GetString());
                }

                foreach (var field in new[] { "message", "detail", "type" })
                {
                    if (error.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String)
                    {
                        return Short(value.GetString());
                    }
                }
            }

            foreach (var field in new[] { "message", "detail" })
            {
                if (root.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    return Short(value.GetString());
                }
            }
        }
        catch (JsonException)
        {
            // kein JSON — dann eben ohne Detail
        }

        return Short(body);

        static string Short(string? text)
        {
            var value = (text ?? "").Replace('\n', ' ').Trim();
            return value.Length > 160 ? value[..160] : value;
        }
    }

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        })
        {
            // Antworten können lange laufen; abgebrochen wird über die Uhr je Aufruf.
            Timeout = Timeout.InfiniteTimeSpan,
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("AIlight/3.0");
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
        return client;
    }
}

/// <summary>Fehler mit lesbarem Satz für die Oberfläche.</summary>
public sealed class AiException : Exception
{
    public AiException(string message, bool retryable, Exception? inner = null)
        : base(message, inner)
    {
        Retryable = retryable;
    }

    public bool Retryable { get; }
}

/// <summary>Der Klient zur Form des Anbieters.</summary>
public static class AiClients
{
    public static AiClient For(ProviderInfo provider) => provider.Shape switch
    {
        ApiShape.Anthropic => new AnthropicClient(),
        ApiShape.Gemini => new GeminiClient(),
        _ => new OpenAiClient(),
    };
}

/// <summary>
/// Trennt Denk-Schritte ab. Die meisten Anbieter schicken sie in einem eigenen
/// Feld; manche (und ältere Antworten) tragen sie als <c>&lt;think&gt;…&lt;/think&gt;</c>
/// im Text — beides wird sauber behandelt.
/// </summary>
public sealed class ThinkSplitter
{
    private const string Open = "<think>";
    private const string Close = "</think>";

    private readonly StringBuilder _reasoning;
    private bool _inside;
    private string _carry = "";

    public ThinkSplitter(StringBuilder reasoning) => _reasoning = reasoning;

    /// <summary>Steht gerade ein Denk-Block offen?</summary>
    public bool Inside => _inside;

    /// <summary>Ein einzelnes Stück trennen. Der Zustand bleibt für das nächste erhalten.</summary>
    public IEnumerable<string> Push(string? content)
    {
        var text = _carry + (content ?? "");
        _carry = "";
        var visible = new List<string>();

        while (text.Length > 0)
        {
            if (_inside)
            {
                var end = text.IndexOf(Close, StringComparison.OrdinalIgnoreCase);
                if (end < 0)
                {
                    // Das Schlusswort kann am Ende angefangen sein — dieser Rest wartet.
                    var keep = TailOfTag(text, Close);
                    var body = text[..(text.Length - keep.Length)];
                    if (body.Length > 0)
                    {
                        _reasoning.Append(body);
                    }

                    _carry = keep;
                    return visible;
                }

                if (end > 0)
                {
                    _reasoning.Append(text[..end]);
                }

                _inside = false;
                text = text[(end + Close.Length)..];
                continue;
            }

            var start = text.IndexOf(Open, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                var keep = TailOfTag(text, Open);
                var body = text[..(text.Length - keep.Length)];
                if (body.Length > 0)
                {
                    visible.Add(body);
                }

                _carry = keep;
                return visible;
            }

            if (start > 0)
            {
                visible.Add(text[..start]);
            }

            _inside = true;
            text = text[(start + Open.Length)..];
        }

        return visible;
    }

    /// <summary>Ein einzelnes Stück trennen — für Aufrufe ohne eigenen Zustand.</summary>
    public static IEnumerable<string> Split(string content, StringBuilder reasoning)
        => new ThinkSplitter(reasoning).Push(content);

    /// <summary>Der längste Schluss von <paramref name="text"/>, der ein Anfang des Worts ist.</summary>
    private static string TailOfTag(string text, string tag)
    {
        var longest = Math.Min(tag.Length - 1, text.Length);
        for (var length = longest; length > 0; length--)
        {
            if (string.Compare(text, text.Length - length, tag, 0, length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return text[(text.Length - length)..];
            }
        }

        return "";
    }
}
