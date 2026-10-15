using System.Net.Http;
using System.Text;
using System.Text.Json;
using AIlight.Core;

namespace AIlight.Ai;

/// <summary>
/// Der Klient für alles, was wie OpenAI spricht: OpenAI selbst, Groq, Mistral,
/// DeepSeek, xAI, OpenRouter, Together, Perplexity, Azure und die lokalen Server
/// Ollama und LM Studio. Sie unterscheiden sich in Kleinigkeiten, und genau die
/// stehen im Katalog: Adresse, Kopfzeile, Name des Token-Felds und die Frage, ob
/// Denk-Stufen und Websuche überhaupt vorgesehen sind.
/// </summary>
public sealed class OpenAiClient : AiClient
{
    protected override HttpRequestMessage Build(AiCall call)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, call.Address + "/chat/completions")
        {
            Content = new StringContent(Payload(call), Encoding.UTF8, "application/json"),
        };

        Authorize(request, call);

        // OpenRouter freut sich über eine Angabe, woher die Anfrage kommt; ohne
        // sie funktioniert es auch, mit ihr taucht die Anwendung dort auf.
        if (call.Provider.Id == "openrouter")
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/rian2acc/AiLight");
            request.Headers.TryAddWithoutValidation("X-OpenRouter-Title", "AIlight");
        }

        return request;
    }

    public override string Payload(AiCall call)
    {
        var request = call.Request;
        var provider = call.Provider;

        using var buffer = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", request.Model);
            writer.WriteBoolean("stream", request.Stream);
            writer.WriteNumber("temperature", request.Temperature);

            // Top-P nur senden, wenn es wirklich gesetzt wurde: manche Anbieter
            // lehnen den Wert 1 ab, andere ignorieren ihn ohnehin.
            if (request.TopP < 1)
            {
                writer.WriteNumber("top_p", request.TopP);
            }

            // Der Name des Felds ist von Anbieter zu Anbieter verschieden:
            // max_completion_tokens (OpenAI, Groq, Azure) gegen max_tokens (Rest).
            writer.WriteNumber(provider.MaxTokensField, request.MaxCompletionTokens);

            if (provider.ReasoningEffort && !string.IsNullOrWhiteSpace(request.ReasoningEffort))
            {
                writer.WriteString("reasoning_effort", request.ReasoningEffort);
            }

            if (request.Stream)
            {
                // Nutzung mitliefern lassen, wo es vorgesehen ist — sonst rechnen
                // wir die Tokens eben selbst.
                writer.WritePropertyName("stream_options");
                writer.WriteStartObject();
                writer.WriteBoolean("include_usage", true);
                writer.WriteEndObject();
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

            if (request.WebSearch && provider.WebSearch)
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

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    protected override void Handle(SseFrame frame, AiStream stream, AiCall call) => Read(frame.Data, stream, call);

    protected override void ParseAnswer(string json, AiStream stream, AiCall call) => Read(json, stream, call);

    /// <summary>
    /// Ein Datenstück (oder eine ganze Antwort) lesen. Beide Formen sind gleich
    /// aufgebaut, deshalb genügt ein Weg: <c>usage</c>, <c>choices[0].finish_reason</c>
    /// und <c>choices[0].delta</c> bzw. <c>choices[0].message</c>.
    /// </summary>
    private static void Read(string data, AiStream stream, AiCall call)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;

            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                stream.SetUsage(Number(usage, "prompt_tokens"), Number(usage, "completion_tokens"));
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                return;
            }

            var choice = choices[0];
            if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String)
            {
                stream.FinishReason = finish.GetString() ?? "";
            }

            // Im Strom heißt es „delta“, als ganze Antwort „message“.
            if (!choice.TryGetProperty("delta", out var piece)
                && !choice.TryGetProperty("message", out piece))
            {
                return;
            }

            var content = Text(piece, "content");
            if (content.Length > 0)
            {
                // AddText erkennt Denk-Blöcke im Text und meldet alles der Oberfläche.
                stream.AddText(content, call.OnText);
            }

            var thinking = Text(piece, "reasoning");
            if (thinking.Length == 0)
            {
                thinking = Text(piece, "reasoning_content");
            }

            if (thinking.Length == 0
                && piece.TryGetProperty("reasoning", out var structured)
                && structured.ValueKind == JsonValueKind.Object)
            {
                thinking = Text(structured, "content");
                if (thinking.Length == 0)
                {
                    thinking = Text(structured, "text");
                }
            }

            if (thinking.Length > 0)
            {
                stream.AddReasoning(thinking, call.OnReasoning);
            }
        }
        catch (JsonException ex)
        {
            Log.Warn($"Datenstück konnte nicht gelesen werden: {ex.Message}");
        }
    }

    private static string Text(JsonElement element, string field)
        => element.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static int Number(JsonElement element, string field)
        => element.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
}
