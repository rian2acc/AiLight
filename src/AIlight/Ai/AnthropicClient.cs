using System.Net.Http;
using System.Text;
using System.Text.Json;
using AIlight.Core;

namespace AIlight.Ai;

/// <summary>
/// Der Klient für Anthropic (Claude). Drei Dinge sind dort anders als bei den
/// OpenAI-kompatiblen Anbietern:
///
/// <list type="bullet">
///   <item>Die System-Vorgabe ist keine Nachricht, sondern das Feld <c>system</c>.</item>
///   <item><c>max_tokens</c> ist Pflicht — ohne es wird die Anfrage abgelehnt.</item>
///   <item>Der Ereignisstrom ist benannt: <c>message_start</c>,
///   <c>content_block_delta</c>, <c>message_delta</c>, <c>message_stop</c>. Die
///   Nutzung wächst dabei mit (sie ist kumulativ, nicht je Stück).</item>
/// </list>
/// </summary>
public sealed class AnthropicClient : AiClient
{
    /// <summary>Die Fassung der Schnittstelle, die dieser Klient spricht.</summary>
    public const string Version = "2023-06-01";

    protected override HttpRequestMessage Build(AiCall call)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, call.Address + "/messages")
        {
            Content = new StringContent(Payload(call), Encoding.UTF8, "application/json"),
        };

        request.Headers.TryAddWithoutValidation("x-api-key", call.ApiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", Version);
        if (call.Request.Stream)
        {
            request.Headers.TryAddWithoutValidation("accept", "text/event-stream");
        }

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
            writer.WriteString("model", request.Model);
            writer.WriteNumber("max_tokens", request.MaxCompletionTokens);
            writer.WriteBoolean("stream", request.Stream);
            writer.WriteNumber("temperature", request.Temperature);
            if (request.TopP < 1)
            {
                writer.WriteNumber("top_p", request.TopP);
            }

            if (request.Thinking)
            {
                // Die aktuelle Form: angepasstes Denken mit zusammengefasster Anzeige.
                writer.WritePropertyName("thinking");
                writer.WriteStartObject();
                writer.WriteString("type", "adaptive");
                writer.WriteString("display", "summarized");
                writer.WriteEndObject();
            }

            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            foreach (var message in request.Messages)
            {
                // System-Nachrichten werden eingesammelt; alles andere geht mit.
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
                writer.WriteString("role", message.Role == "assistant" ? "assistant" : "user");
                writer.WriteString("content", message.Content);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            if (system.Length > 0)
            {
                writer.WriteString("system", system.ToString());
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    protected override void Handle(SseFrame frame, AiStream stream, AiCall call)
    {
        if (frame.Data.Length == 0)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(frame.Data);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var kind) && kind.ValueKind == JsonValueKind.String
                ? kind.GetString() ?? frame.Event
                : frame.Event;

            switch (type)
            {
                case "message_start":
                    if (root.TryGetProperty("message", out var message)
                        && message.TryGetProperty("usage", out var first))
                    {
                        stream.SetUsage(Number(first, "input_tokens"), Number(first, "output_tokens"));
                    }

                    break;

                case "content_block_delta":
                    if (!root.TryGetProperty("delta", out var delta))
                    {
                        return;
                    }

                    var blockType = Text(delta, "type");
                    if (blockType == "thinking_delta")
                    {
                        stream.AddReasoning(Text(delta, "thinking"), call.OnReasoning);
                    }
                    else if (blockType == "text_delta")
                    {
                        stream.AddText(Text(delta, "text"), call.OnText);
                    }

                    // signature_delta und input_json_delta gehören nicht in die Antwort.
                    break;

                case "message_delta":
                    if (root.TryGetProperty("delta", out var final)
                        && final.TryGetProperty("stop_reason", out var reason)
                        && reason.ValueKind == JsonValueKind.String)
                    {
                        stream.FinishReason = Map(reason.GetString() ?? "");
                    }

                    if (root.TryGetProperty("usage", out var usage))
                    {
                        stream.SetUsage(Number(usage, "input_tokens"), Number(usage, "output_tokens"));
                    }

                    break;

                case "error":
                    var text = root.TryGetProperty("error", out var error) ? Text(error, "message") : "";
                    Log.Warn($"Anthropic meldet einen Fehler im Strom: {text}");
                    stream.FinishReason = "error";
                    break;
            }
        }
        catch (JsonException ex)
        {
            Log.Warn($"Anthropic-Rahmen konnte nicht gelesen werden: {ex.Message}");
        }
    }

    protected override void ParseAnswer(string json, AiStream stream, AiCall call)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("content", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in blocks.EnumerateArray())
                {
                    var kind = Text(block, "type");
                    if (kind == "text")
                    {
                        stream.AddText(Text(block, "text"), call.OnText);
                    }
                    else if (kind == "thinking")
                    {
                        stream.AddReasoning(Text(block, "thinking"), call.OnReasoning);
                    }
                }
            }

            if (root.TryGetProperty("usage", out var usage))
            {
                stream.SetUsage(Number(usage, "input_tokens"), Number(usage, "output_tokens"));
            }

            stream.FinishReason = Map(Text(root, "stop_reason"));

            if (root.TryGetProperty("error", out var error))
            {
                throw new AiException(
                    $"Anthropic: {Text(error, "message")}", retryable: false);
            }
        }
        catch (JsonException ex)
        {
            throw new AiException($"Die Antwort von Anthropic war nicht lesbar: {ex.Message}", retryable: false, ex);
        }
    }

    protected override bool WasTruncated(string finishReason)
        => finishReason is "max_tokens" or "length";

    /// <summary>Anthropics Gründe in die eigenen Worte übersetzen.</summary>
    private static string Map(string reason) => reason switch
    {
        "max_tokens" => "length",
        "end_turn" or "stop_sequence" => "stop",
        "tool_use" => "tool_use",
        "refusal" => "refusal",
        _ => reason,
    };

    private static string Text(JsonElement element, string field)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(field, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static int Number(JsonElement element, string field)
        => element.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
}
