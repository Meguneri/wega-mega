using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Robust.Shared.Log;

namespace Content.Server._Wega.LlmNpc;

/// <summary>
/// Веб-поиск для NPC через бесключевой Instant Answer API DuckDuckGo. Возвращает краткую сводку
/// (ответ + аннотация + пара связанных тем) или null, если ничего внятного не нашлось — тогда NPC
/// честно скажет, что не знает, вместо выдумки. Покрытие ограничено (энциклопедические/сущностные
/// запросы), новости и совсем свежее API не отдаёт; при желании источник легко заменить.
/// </summary>
public sealed class LlmSearch
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public async Task<string?> SearchAsync(string query, ISawmill sawmill)
    {
        query = query.Trim();
        if (query.Length is 0 or > 200)
            return null;

        var url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1&skip_disambig=1";
        try
        {
            var raw = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var sb = new StringBuilder();

            if (Get(root, "Answer") is { } answer)
                sb.AppendLine(answer);
            if (Get(root, "AbstractText") is { } abstractText)
                sb.AppendLine(abstractText);

            if (root.TryGetProperty("RelatedTopics", out var topics) && topics.ValueKind == JsonValueKind.Array)
            {
                var n = 0;
                foreach (var topic in topics.EnumerateArray())
                {
                    if (n >= 4)
                        break;
                    if (Get(topic, "Text") is { } text)
                    {
                        sb.AppendLine("- " + text);
                        n++;
                    }
                }
            }

            var result = sb.ToString().Trim();
            sawmill.Info($"веб-поиск «{query}» → {(result.Length > 0 ? $"{result.Length} симв." : "пусто")}");
            return result.Length > 0 ? result : null;
        }
        catch (Exception e)
        {
            sawmill.Warning($"веб-поиск «{query}» упал: {e.Message}");
            return null;
        }
    }

    private static string? Get(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s
            ? s
            : null;
}
