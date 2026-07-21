using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Robust.Shared.Log;

namespace Content.Server._Wega.LlmNpc;

/// <summary>Структурный ответ модели: что сказать, какой эмоут, что запомнить.</summary>
public sealed class LlmReply
{
    public string? Say;
    public string? Emote;
    public string? Remember;
    /// <summary>Сказать реплику шёпотом (слышно только вплотную) вместо обычной речи.</summary>
    public bool Whisper;
}

/// <summary>
/// Инструмент, который модель может вызвать в ходе диалога (веб-поиск, «смешать напиток» и т.п.).
/// <see cref="Declaration"/> — объявление функции для API; <see cref="Invoke"/> получает строку
/// аргументов (JSON от модели) и возвращает текстовый результат, который уйдёт обратно в диалог.
/// </summary>
public sealed class LlmTool
{
    public required string Name;
    public required object Declaration;
    public required Func<string?, Task<string?>> Invoke;
}

/// <summary>
/// Обращение к OpenAI-совместимому chat-completions API (OpenAI / OpenRouter / Gemini-compat / прочие).
/// Модель просят вернуть JSON {"say","emote","remember"} — вытягиваем его из текста ответа.
///
/// Если переданы инструменты, они объявляются модели: та сама решает, когда их вызвать; сервер
/// исполняет вызов, отдаёт результат обратно, и модель отвечает уже с учётом него (tool-calling).
/// </summary>
public sealed class LlmBackend
{
    // Один HttpClient на всё приложение (создавать по запросу — антипаттерн, исчерпывает сокеты).
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Ведёт диалог с моделью (до нескольких раундов, если она вызывает инструменты) и возвращает
    /// разобранный ответ или null при любой ошибке — вызывающий тогда просто молчит.
    /// </summary>
    public async Task<LlmReply?> AskAsync(string endpoint, string apiKey, string model,
        string system, string context, ISawmill sawmill, IReadOnlyList<LlmTool>? tools,
        Action<int, int, int>? onUsage = null)
    {
        var messages = new List<object>
        {
            new { role = "system", content = system },
            new { role = "user", content = context },
        };

        var hasTools = tools is { Count: > 0 };

        // До 4 раундов: модель может вызвать инструмент, получить результат и лишь потом ответить.
        // Обычно хватает одного вызова; лимит защищает от зацикливания на инструментах.
        const int maxRounds = 4;
        for (var round = 0; round < maxRounds; round++)
        {
            // Последний раунд — принудительно без инструментов: мелкие модели иначе дёргают их
            // цепочкой до упора и так и не отвечают текстом (NPC молчит, а действия дублируются).
            var toolsAllowed = hasTools && round < maxRounds - 1;
            // JSON-режим API: модель обязана вернуть валидный JSON-объект (наш {say,emote,remember}).
            // Без него gpt-4o после инструментов стабильно отвечал простым текстом.
            var jsonFormat = new { type = "json_object" };
            // Рассуждающие модели OpenAI (gpt-5*, o1/o3/o4*) не принимают temperature — шлём без него.
            var reasoning = model.StartsWith("gpt-5") || model.StartsWith("o1") || model.StartsWith("o3")
                || model.StartsWith("o4") || model.Contains("/gpt-5") || model.Contains("/o1") || model.Contains("/o3");
            if (hasTools && !toolsAllowed)
            {
                // Прямой пинок: без него мелкие модели и на последнем раунде возвращают пустой content.
                messages.Add(new
                {
                    role = "system",
                    content = "Инструменты больше недоступны. Ответь СЕЙЧАС финальным JSON-объектом " +
                              "{\"say\",\"emote\",\"remember\"} — скажи собеседнику, что сделала или почему не вышло.",
                });
                sawmill.Info("финальный раунд: инструменты отключены, требуем текстовый ответ");
            }
            // Рассуждающим моделям max_tokens тоже мал: они тратят токены на «мысли» до ответа.
            // Штрафы за повтор: главный рычаг против «NPC говорит одно и то же». Рассуждающие
            // модели (gpt-5/o*) их не принимают — им шлём без штрафов, как и без temperature.
            const double freqPenalty = 0.6;
            const double presPenalty = 0.4;

            object body = (toolsAllowed, reasoning) switch
            {
                (true, false) => new { model, messages, temperature = 0.8, max_tokens = 500,
                    frequency_penalty = freqPenalty, presence_penalty = presPenalty,
                    tools = tools!.Select(t => t.Declaration), tool_choice = "auto", response_format = jsonFormat },
                (false, false) => new { model, messages, temperature = 0.8, max_tokens = 400,
                    frequency_penalty = freqPenalty, presence_penalty = presPenalty,
                    response_format = jsonFormat },
                // reasoning_effort=low: болтовня в баре не стоит долгих размышлений — отвечает быстрее.
                (true, true) => new { model, messages, max_completion_tokens = 2000,
                    reasoning_effort = "low",
                    tools = tools!.Select(t => t.Declaration), tool_choice = "auto", response_format = jsonFormat },
                (false, true) => new { model, messages, max_completion_tokens = 1500,
                    reasoning_effort = "low",
                    response_format = jsonFormat },
            };

            var raw = await PostAsync(endpoint, apiKey, body, sawmill);
            if (raw == null)
                return null;

            JsonElement message;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                ReportUsage(doc.RootElement, onUsage);
                // Clone — чтобы элемент пережил dispose документа и его можно было переслать обратно.
                message = doc.RootElement.GetProperty("choices")[0].GetProperty("message").Clone();
            }
            catch (Exception e)
            {
                sawmill.Warning($"разбор ответа не удался: {e.Message}");
                return null;
            }

            // Модель хочет вызвать инструмент?
            if (toolsAllowed
                && message.TryGetProperty("tool_calls", out var toolCalls)
                && toolCalls.ValueKind == JsonValueKind.Array
                && toolCalls.GetArrayLength() > 0)
            {
                // Ассистентское сообщение с его tool_calls обязано быть в истории ПЕРЕД ответами tool.
                messages.Add(new { role = "assistant", content = (string?)null, tool_calls = toolCalls });

                foreach (var call in toolCalls.EnumerateArray())
                {
                    var id = call.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    var (name, args) = ReadCall(call);
                    var tool = tools!.FirstOrDefault(t => t.Name == name);

                    string result;
                    if (tool == null)
                        result = "Неизвестный инструмент.";
                    else
                    {
                        try
                        {
                            result = await tool.Invoke(args) ?? "Ничего не найдено.";
                        }
                        catch (Exception e)
                        {
                            sawmill.Warning($"инструмент {name} упал: {e.Message}");
                            result = "Инструмент дал сбой.";
                        }
                    }

                    sawmill.Info($"инструмент {name}({args}) → {result}");
                    messages.Add(new { role = "tool", tool_call_id = id, content = result });
                }

                continue; // следующий раунд — модель ответит уже с результатами
            }

            // Финальный ответ: content с JSON.
            var content = message.TryGetProperty("content", out var c) ? c.GetString() : null;
            if (string.IsNullOrWhiteSpace(content))
            {
                sawmill.Warning("ответ модели без текста");
                return null;
            }

            var json = ExtractJson(content);
            if (json == null)
            {
                // Модель ответила простым текстом вместо JSON — не выбрасываем (NPC иначе молчит),
                // а используем текст как реплику. Лучше «не тот формат», чем тишина после действия.
                sawmill.Warning($"в ответе нет JSON-объекта, используем как реплику: {content}");
                return new LlmReply { Say = content.Trim() };
            }

            try
            {
                using var inner = JsonDocument.Parse(json);
                var root = inner.RootElement;
                return new LlmReply
                {
                    Say = GetStr(root, "say"),
                    Emote = GetStr(root, "emote"),
                    Remember = GetStr(root, "remember"),
                    Whisper = GetBool(root, "whisper"),
                };
            }
            catch (Exception e)
            {
                sawmill.Warning($"разбор JSON не удался ({e.Message}), используем текст как реплику");
                return new LlmReply { Say = content.Trim() };
            }
        }

        sawmill.Warning("модель зациклилась на инструментах, финального ответа нет");
        return null;
    }

    /// <summary>
    /// Простой текстовый запрос без инструментов и без JSON-обёртки — возвращает сырой content ответа.
    /// Используется для оффлайн-задач вроде дистилляции стиля из логов (один тяжёлый проход, не в диалоге).
    /// </summary>
    public async Task<string?> AskRawAsync(string endpoint, string apiKey, string model,
        string system, string user, ISawmill sawmill)
    {
        var messages = new List<object>
        {
            new { role = "system", content = system },
            new { role = "user", content = user },
        };
        var body = new { model, messages, temperature = 0.4, max_tokens = 700 };

        var raw = await PostAsync(endpoint, apiKey, body, sawmill);
        if (raw == null)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message")
                .GetProperty("content").GetString();
        }
        catch (Exception e)
        {
            sawmill.Warning($"разбор raw-ответа не удался: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Одиночный «сырой» вызов без инструментов и JSON-режима: system + user → текст ответа.
    /// Используется служебными задачами (консолидация памяти), не диалогом.
    /// </summary>
    public async Task<string?> CompleteTextAsync(string endpoint, string apiKey, string model,
        string system, string user, ISawmill sawmill, Action<int, int, int>? onUsage = null)
    {
        var reasoning = model.StartsWith("gpt-5") || model.StartsWith("o1") || model.StartsWith("o3")
            || model.StartsWith("o4") || model.Contains("/gpt-5") || model.Contains("/o1") || model.Contains("/o3");

        var messages = new object[]
        {
            new { role = "system", content = system },
            new { role = "user", content = user },
        };
        object body = reasoning
            ? new { model, messages, max_completion_tokens = 3000 }
            : new { model, messages, temperature = 0.2, max_tokens = 2000 };

        var raw = await PostAsync(endpoint, apiKey, body, sawmill);
        if (raw == null)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            ReportUsage(doc.RootElement, onUsage);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message")
                .GetProperty("content").GetString();
        }
        catch (Exception e)
        {
            sawmill.Warning($"разбор служебного ответа не удался: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Один POST к API с ретраями: сетевые обрывы («Error while copying content to a stream»)
    /// и 429/5xx пробуем ещё дважды с паузой — нестабильный канал не должен превращаться в молчание NPC.
    /// </summary>
    private async Task<string?> PostAsync(string endpoint, string apiKey, object body, ISawmill sawmill)
    {
        var payload = JsonSerializer.Serialize(body);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromSeconds(attempt)); // 1с, 2с

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            HttpResponseMessage resp;
            try
            {
                resp = await Http.SendAsync(req);
            }
            catch (Exception e)
            {
                sawmill.Warning($"HTTP-запрос к {endpoint} упал (попытка {attempt + 1}/3): {e.Message}");
                continue;
            }

            var raw = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode)
                return raw;

            var snippet = raw.Length > 400 ? raw[..400] : raw;
            sawmill.Warning($"API вернул {(int)resp.StatusCode} {resp.StatusCode} (попытка {attempt + 1}/3): {snippet}");

            // Ретраим только временное: перегрузку и серверные ошибки. 4xx — наша ошибка, повтор бессмыслен.
            var code = (int)resp.StatusCode;
            if (code != 429 && code < 500)
                return null;
        }

        return null;
    }

    /// <summary>
    /// Точный расход из блока usage ответа: prompt_tokens / completion_tokens и кэшированная часть
    /// промпта (prompt_tokens_details.cached_tokens — биллится дешевле). Это те же числа, по которым
    /// провайдер выставляет счёт; наш локальный подсчёт был бы лишь оценкой.
    /// </summary>
    private static void ReportUsage(JsonElement root, Action<int, int, int>? onUsage)
    {
        if (onUsage == null || !root.TryGetProperty("usage", out var usage)
            || usage.ValueKind != JsonValueKind.Object)
            return;

        var prompt = ReadInt(usage, "prompt_tokens");
        var completion = ReadInt(usage, "completion_tokens");
        var cached = 0;
        if (usage.TryGetProperty("prompt_tokens_details", out var details)
            && details.ValueKind == JsonValueKind.Object)
            cached = ReadInt(details, "cached_tokens");

        onUsage(prompt, cached, completion);
    }

    private static int ReadInt(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt32(out var value)
            ? value
            : 0;

    /// <summary>Достаёт имя функции и строку аргументов (JSON) из tool-вызова.</summary>
    private static (string? name, string? args) ReadCall(JsonElement toolCall)
    {
        try
        {
            var fn = toolCall.GetProperty("function");
            var name = fn.TryGetProperty("name", out var n) ? n.GetString() : null;
            var args = fn.TryGetProperty("arguments", out var a) ? a.GetString() : null;
            return (name, args);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>Достаёт строковый аргумент по ключу из JSON-строки аргументов инструмента.</summary>
    public static string? Arg(string? argsJson, string key)
    {
        if (string.IsNullOrWhiteSpace(argsJson))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            return doc.RootElement.TryGetProperty(key, out var v) ? v.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetStr(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    // Мелкие модели иногда шлют bool строкой ("true"); принимаем оба варианта.
    private static bool GetBool(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var v)
            && (v.ValueKind == JsonValueKind.True
                || (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b) && b));

    /// <summary>
    /// Вытягивает JSON-объект из текста ответа: снимает markdown-ограждение (```json … ```) и берёт
    /// подстроку от первой '{' до последней '}'. Модели любят оборачивать JSON в код-блок или пояснения.
    /// </summary>
    private static string? ExtractJson(string content)
    {
        content = content.Trim();

        if (content.StartsWith("```"))
        {
            var nl = content.IndexOf('\n');
            if (nl >= 0)
                content = content[(nl + 1)..];
            if (content.EndsWith("```"))
                content = content[..^3];
            content = content.Trim();
        }

        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        return content.Substring(start, end - start + 1);
    }
}
