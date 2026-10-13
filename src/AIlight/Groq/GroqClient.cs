using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIlight.Core;

namespace AIlight.Groq;

public sealed record ChatMessage(string Role, string Content);

/// <summary>Eine Frage an Groq — fertig zusammengesetzt.</summary>
public sealed class AskRequest
{
    public required string Model { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public int MaxCompletionTokens { get; init; } = 2048;
    public string? ReasoningEffort { get; init; }
    public bool WebSearch { get; init; }
    public double Temperature { get; init; } = 0.7;
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
/// Der eine HTTP-Client: eine Instanz, offene Verbindungen werden
/// wiederverwendet, Streaming über SSE, Wiederholung bei 429/5xx.
/// </summary>
public sealed class GroqClient : IDisposable
{
    private const string Endpoint = "https://api.groq.com/openai/v1/chat/completions";
    private const int MaxAttempts = 3;

    private readonly HttpClient _http;

    public GroqClient()
    {
        _http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        })
        {
            // Antworten können lange laufen; der Abbruch läuft über die CancellationToken-Quelle.
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AIlight/1.0");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
    }

    public async Task<AskResult> StreamAsync(
        string apiKey,
        AskRequest request,
        Action<string> onText,
        Action<string>? onReasoning,
        CancellationToken token)
    {
        var payload = BuildPayload(request);
        var started = System.Diagnostics.Stopwatch.StartNew();
        var text = new StringBuilder();
        var reasoning = new StringBuilder();
        var attempt = 0;

        while (true)
        {
            attempt++;
            token.ThrowIfCancellationRequested();
            var sentAnyText = false;
            var finishReason = "";
            var usageIn = 0;
            var usageOut = 0;

            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                };
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                using var response = await _http
                    .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    var retryDelay = RetryDelay(response, attempt);
                    if (retryDelay is { } delay && !sentAnyText)
                    {
                        Log.Warn($"Groq antwortete {(int)response.StatusCode} — neuer Versuch in {delay.TotalSeconds:0.0} s");
                        await Task.Delay(delay, token).ConfigureAwait(false);
                        continue;
                    }

                    throw new GroqException(Describe(response.StatusCode, body, response), retryDelay is null);
                }

                await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                while (!token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (line.Length == 0 || !line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var data = line[5..].Trim();
                    if (data.Length == 0)
                    {
                        continue;
                    }

                    if (data == "[DONE]")
                    {
                        break;
                    }

                    ReadChunk(data, text, reasoning, onText, onReasoning, ref finishReason,
                        ref usageIn, ref usageOut, ref sentAnyText);
                }

                started.Stop();
                return Finish(request, text, reasoning, finishReason, usageIn, usageOut, started, false);
            }
            catch (OperationCanceledException)
            {
                // Abbruch durch den Nutzer: der Text bleibt stehen.
                started.Stop();
                return Finish(request, text, reasoning, finishReason: "cancelled", usageIn, usageOut, started, true);
            }
            catch (HttpRequestException ex)
            {
                if (attempt < MaxAttempts)
                {
                    var delay = TimeSpan.FromSeconds(Math.Min(10, 1.5 * attempt));
                    Log.Warn($"Netzfehler ({ex.Message}) — neuer Versuch in {delay.TotalSeconds:0.0} s");
                    await Task.Delay(delay, token).ConfigureAwait(false);
                    continue;
                }

                throw new GroqException("Keine Verbindung zu Groq. Bitte Internetverbindung prüfen.", retryable: true, ex);
            }
        }
    }

    private AskResult Finish(
        AskRequest request, StringBuilder text, StringBuilder reasoning, string finishReason,
        int usageIn, int usageOut, System.Diagnostics.Stopwatch watch, bool cancelled)
    {
        var model = GroqModels.ById(request.Model);
        var textValue = text.ToString();
        var inputTokens = usageIn > 0
            ? usageIn
            : GroqModels.EstimateTokens(request.Messages.Sum(m => m.Content.Length) + 20);
        var outputTokens = usageOut > 0 ? usageOut : GroqModels.EstimateTokens(textValue.Length + reasoning.Length);
        return new AskResult(
            textValue,
            reasoning.ToString(),
            request.Model,
            inputTokens,
            outputTokens,
            model.Cost(inputTokens, outputTokens),
            watch.Elapsed,
            finishReason,
            cancelled,
            Truncated: finishReason == "length");
    }

    private static void ReadChunk(
        string data, StringBuilder text, StringBuilder reasoning,
        Action<string> onText, Action<string>? onReasoning,
        ref string finishReason, ref int usageIn, ref int usageOut, ref bool sentAnyText)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;

            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("prompt_tokens", out var prompt)) usageIn = prompt.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var completion)) usageOut = completion.GetInt32();
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                return;
            }

            var choice = choices[0];
            if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String)
            {
                finishReason = finish.GetString() ?? "";
            }

            if (!choice.TryGetProperty("delta", out var delta))
            {
                return;
            }

            if (delta.TryGetProperty("reasoning", out var thinking) && thinking.ValueKind == JsonValueKind.String)
            {
                var piece = thinking.GetString() ?? "";
                if (piece.Length > 0)
                {
                    reasoning.Append(piece);
                    onReasoning?.Invoke(piece);
                }
            }

            if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                foreach (var piece in ThinkSplitter.Split(content.GetString() ?? "", reasoning))
                {
                    text.Append(piece);
                    sentAnyText = true;
                    onText(piece);
                }
            }
        }
        catch (JsonException ex)
        {
            Log.Warn($"Datenstück konnte nicht gelesen werden: {ex.Message}");
        }
    }

    internal static string BuildPayload(AskRequest request)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", request.Model);
            writer.WriteBoolean("stream", true);
            writer.WriteNumber("temperature", request.Temperature);
            writer.WriteNumber("max_completion_tokens", request.MaxCompletionTokens);
            if (!string.IsNullOrWhiteSpace(request.ReasoningEffort))
            {
                writer.WriteString("reasoning_effort", request.ReasoningEffort);
            }

            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            foreach (var message in request.Messages)
            {
                writer.WriteStartObject();
                writer.WriteString("role", message.Role);
                writer.WriteString("content", message.Content);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            if (request.WebSearch)
            {
                writer.WritePropertyName("tools");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("type", "browser_search");
                writer.WriteEndObject();
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Nach welcher Zeit ein neuer Versuch sinnvoll ist (<c>null</c> = aufgeben).</summary>
    private static TimeSpan? RetryDelay(HttpResponseMessage response, int attempt)
    {
        var status = (int)response.StatusCode;
        var retryable = status is 429 or 500 or 502 or 503 or 504 or 498;
        if (!retryable || attempt >= MaxAttempts)
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
    private static string Describe(HttpStatusCode status, string body, HttpResponseMessage response)
    {
        var detail = ReadErrorMessage(body);
        var suffix = detail.Length > 0 ? $" („{detail}“)" : "";
        return (int)status switch
        {
            400 => "Groq hat die Anfrage abgelehnt (400)" + suffix,
            401 => "Der API-Schlüssel wird abgelehnt (401)" + suffix
                   + " — bitte im Einstellungsfenster prüfen.",
            403 => "Der Zugriff wurde verweigert (403)" + suffix,
            404 => "Das gewählte Modell ist nicht verfügbar (404)" + suffix,
            413 => "Die Anfrage ist zu groß (413)" + suffix + " — bitte ein neues Gespräch starten.",
            422 => "Groq konnte die Anfrage nicht verarbeiten (422)" + suffix,
            429 => "Zu viele Anfragen (429)" + suffix + " — bitte kurz warten.",
            498 => "Groq hat gerade keine freie Kapazität (498) — bitte später erneut versuchen.",
            >= 500 => "Groq meldet einen Serverfehler (" + (int)status + ")" + suffix + " — bitte erneut versuchen.",
            _ => $"Groq hat mit Fehler {(int)status} geantwortet{suffix}",
        };
    }

    private static string ReadErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
            {
                var text = message.GetString() ?? "";
                return text.Length > 160 ? text[..160] : text;
            }
        }
        catch (JsonException)
        {
            // kein JSON — dann eben ohne Detail
        }

        return "";
    }

    /// <summary>Key-Test: eine winzige echte Anfrage.</summary>
    public async Task<string> TestKeyAsync(string apiKey, CancellationToken token)
    {
        var request = new AskRequest
        {
            Model = GroqModels.Small.Id,
            Messages = new[] { new ChatMessage("user", "Antworte mit genau einem Wort: OK") },
            MaxCompletionTokens = 16,
            Temperature = 0,
        };

        var result = await StreamAsync(apiKey, request, _ => { }, null, token).ConfigureAwait(false);
        var answer = result.Text.Trim();
        return answer.Length == 0
            ? "Der Schlüssel funktioniert (Antwort ohne Text)."
            : $"Der Schlüssel funktioniert — Antwort: „{answer}“ ({result.Duration.TotalSeconds:0.0} s).";
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Fehler mit lesbarem Satz für die Oberfläche.</summary>
public sealed class GroqException : Exception
{
    public GroqException(string message, bool retryable, Exception? inner = null)
        : base(message, inner)
    {
        Retryable = retryable;
    }

    public bool Retryable { get; }
}

/// <summary>
/// Trennt Denk-Schritte ab. Groq liefert sie bei gpt-oss normalerweise im Feld
/// <c>reasoning</c>; ältere Antworten können sie als <c>&lt;think&gt;…&lt;/think&gt;</c>
/// im Text tragen — beides wird sauber behandelt.
/// </summary>
public static class ThinkSplitter
{
    private const string Open = "<think>";
    private const string Close = "</think>";

    public static IEnumerable<string> Split(string content, StringBuilder reasoning)
    {
        if (content.Length == 0)
        {
            yield break;
        }

        var rest = content;
        while (rest.Length > 0)
        {
            var start = rest.IndexOf(Open, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                yield return rest;
                yield break;
            }

            if (start > 0)
            {
                yield return rest[..start];
            }

            var end = rest.IndexOf(Close, start, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                reasoning.Append(rest[(start + Open.Length)..]);
                yield break;
            }

            reasoning.Append(rest[(start + Open.Length)..end]);
            rest = rest[(end + Close.Length)..];
        }
    }
}
