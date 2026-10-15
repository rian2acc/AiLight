using System.Net.Http;
using System.Text;
using System.Text.Json;
using AIlight.Core;

namespace AIlight.Ai;

/// <summary>
/// Der Klient für Google Gemini. Auch hier ist einiges anders:
///
/// <list type="bullet">
///   <item>Der Schlüssel reist in der Kopfzeile <c>x-goog-api-key</c> (nicht als Bearer).</item>
///   <item>Das Modell steht in der Adresse: <c>/models/{modell}:streamGenerateContent?alt=sse</c>.</item>
///   <item>Das Gespräch heißt <c>contents</c>, die System-Vorgabe <c>systemInstruction</c>,
///   und die Rolle des Assistenten heißt <c>model</c> statt <c>assistant</c>.</item>
///   <item>Die Antwortteile liegen unter <c>candidates[].content.parts[].text</c>;
///   Teile mit <c>thought: true</c> sind Denk-Schritte.</item>
/// </list>
/// </summary>
public sealed class GeminiClient : AiClient
{
    /// <summary>Wie viel Platz die Denk-Schritte bekommen dürfen (Gemini 2.5).</summary>
    public const int ThinkingBudget = 4096;

    protected override HttpRequestMessage Build(AiCall call)
    {
        var method = call.Request.Stream ? ":streamGenerateContent?alt=sse" : ":generateContent";
        var address = $"{call.Address}/models/{Uri.EscapeDataString(call.Request.Model)}{method}";

        var request = new HttpRequestMessage(HttpMethod.Post, address)
        {
            Content = new StringContent(Payload(call), Encoding.UTF8, "application/json"),
        };

        request.Headers.TryAddWithoutValidation("x-goog-api-key", call.ApiKey);
        return request;
    }

    public override string Payload(AiCall call)
    {
        var request = call.Request;
        var system = new StringBuilder();

        using var buffer = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            writer.WritePropertyName("contents");
            writer.WriteStartArray();
            foreach (var message in request.Messages)
            {
                if (message.Role == "system")
                {
                    if (system.Length > 0)
                    {
                        system.Append("\n\n");
                    }

                    system.Append(message.Content);
                    continue;
                }

                writer.WriteStartObject();
                writer.WriteString("role", message.Role == "assistant" ? "model" : "user");
                writer.WritePropertyName("parts");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("text", message.Content);
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            if (system.Length > 0)
            {
                writer.WritePropertyName("systemInstruction");
                writer.WriteStartObject();
                writer.WritePropertyName("parts");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("text", system.ToString());
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WritePropertyName("generationConfig");
            writer.WriteStartObject();
            writer.WriteNumber("temperature", request.Temperature);
            if (request.TopP < 1)
            {
                writer.WriteNumber("topP", request.TopP);
            }

            writer.WriteNumber("maxOutputTokens", request.MaxCompletionTokens);
            if (request.Thinking)
            {
                writer.WritePropertyName("thinkingConfig");
                writer.WriteStartObject();
                writer.WriteNumber("thinkingBudget", ThinkingBudget);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    protected override void Handle(SseFrame frame, AiStream stream, AiCall call) => Read(frame.Data, stream, call);

    protected override void ParseAnswer(string json, AiStream stream, AiCall call) => Read(json, stream, call);

    private static void Read(string data, AiStream stream, AiCall call)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;

            if (root.TryGetProperty("usageMetadata", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                stream.SetUsage(Number(usage, "promptTokenCount"), Number(usage, "candidatesTokenCount"));
            }

            // Wurde die Anfrage schon vorher abgelehnt, steht das nicht in den Kandidaten.
            if (root.TryGetProperty("promptFeedback", out var feedback)
                && feedback.TryGetProperty("blockReason", out var block)
                && block.ValueKind == JsonValueKind.String)
            {
                var reason = block.GetString() ?? "";
                Log.Warn($"Gemini hat die Anfrage blockiert: {reason}");
                stream.FinishReason = "blocked";
                if (!stream.Delivered)
                {
                    throw new AiException(
                        $"Gemini hat die Anfrage blockiert ({reason}). Bitte anders formulieren.",
                        retryable: false);
                }
            }

            if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var candidate in candidates.EnumerateArray())
            {
                if (candidate.TryGetProperty("finishReason", out var finish) && finish.ValueKind == JsonValueKind.String)
                {
                    stream.FinishReason = finish.GetString() ?? "";
                }

                if (!candidate.TryGetProperty("content", out var content)
                    || !content.TryGetProperty("parts", out var parts)
                    || parts.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var part in parts.EnumerateArray())
                {
                    var text = Text(part, "text");
                    if (text.Length == 0)
                    {
                        continue;
                    }

                    // Teile mit „thought“ sind Denk-Schritte, keine Antwort.
                    if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)
                    {
                        stream.AddReasoning(text, call.OnReasoning);
                    }
                    else
                    {
                        stream.AddText(text, call.OnText);
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            Log.Warn($"Gemini-Rahmen konnte nicht gelesen werden: {ex.Message}");
        }
    }

    protected override bool WasTruncated(string finishReason)
        => finishReason is "MAX_TOKENS" or "length" or "max_tokens";

    private static string Text(JsonElement element, string field)
        => element.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static int Number(JsonElement element, string field)
        => element.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
}
