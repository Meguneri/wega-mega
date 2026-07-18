using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Content.Server._Wega.LlmNpc;

/// <summary>
/// Счётчик расхода API за раунд: точные токены из блока usage каждого ответа (включая кэшированные
/// токены промпта — они биллятся дешевле), сгруппированные по «NPC | ключ | модель». Стоимость
/// считается по прайс-таблице из cvar'а llm_npc_prices; потокобезопасен (ответы приходят из пула).
/// </summary>
public sealed class LlmNpcUsage
{
    /// <summary>Расход одной связки «NPC | хвост ключа | модель».</summary>
    public sealed class Entry
    {
        public int Requests;
        public long PromptTokens;
        public long CachedTokens;
        public long CompletionTokens;
    }

    private readonly object _lock = new();
    private readonly Dictionary<(string Npc, string Key, string Model), Entry> _entries = new();

    // Общий срез «за всё время»: не сбрасывается по раундам, переживает рестарты через usage.json.
    private readonly Dictionary<(string Npc, string Key, string Model), Entry> _lifetime = new();

    /// <summary>Есть несохранённые изменения общего среза (для троттлинга записи на диск).</summary>
    public bool Dirty { get; private set; }

    /// <summary>Записывает usage одного HTTP-ответа (в tool-цикле их несколько на раздумье).</summary>
    public void Record(string npc, string keySuffix, string model, int prompt, int cached, int completion)
    {
        lock (_lock)
        {
            Bump(_entries, (npc, keySuffix, model), prompt, cached, completion);
            Bump(_lifetime, (npc, keySuffix, model), prompt, cached, completion);
            Dirty = true;
        }
    }

    private static void Bump(Dictionary<(string, string, string), Entry> dict,
        (string, string, string) key, int prompt, int cached, int completion)
    {
        if (!dict.TryGetValue(key, out var entry))
        {
            entry = new Entry();
            dict[key] = entry;
        }
        entry.Requests++;
        entry.PromptTokens += prompt;
        entry.CachedTokens += cached;
        entry.CompletionTokens += completion;
    }

    /// <summary>Сброс раундового счётчика на рестарте (общий срез не трогаем).</summary>
    public void Reset()
    {
        lock (_lock)
            _entries.Clear();
    }

    // ------------------------------------------------------------------ персистентность среза

    private sealed class SavedEntry
    {
        public string Npc { get; set; } = "";
        public string Key { get; set; } = "";
        public string Model { get; set; } = "";
        public int Requests { get; set; }
        public long PromptTokens { get; set; }
        public long CachedTokens { get; set; }
        public long CompletionTokens { get; set; }
    }

    /// <summary>Сохраняет общий срез в JSON (llm_npc/usage.json в data-папке).</summary>
    public void Save(string path)
    {
        List<SavedEntry> snapshot;
        lock (_lock)
        {
            snapshot = _lifetime.Select(kv => new SavedEntry
            {
                Npc = kv.Key.Npc,
                Key = kv.Key.Key,
                Model = kv.Key.Model,
                Requests = kv.Value.Requests,
                PromptTokens = kv.Value.PromptTokens,
                CachedTokens = kv.Value.CachedTokens,
                CompletionTokens = kv.Value.CompletionTokens,
            }).ToList();
            Dirty = false;
        }

        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(snapshot,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Статистика — не критичный путь; в худшем случае срез отстанет до следующей записи.
        }
    }

    /// <summary>Загружает общий срез при старте сервера (нет файла — начинаем с нуля).</summary>
    public void Load(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path))
                return;
            var saved = System.Text.Json.JsonSerializer.Deserialize<List<SavedEntry>>(
                System.IO.File.ReadAllText(path));
            if (saved == null)
                return;

            lock (_lock)
            {
                _lifetime.Clear();
                foreach (var e in saved)
                {
                    _lifetime[(e.Npc, e.Key, e.Model)] = new Entry
                    {
                        Requests = e.Requests,
                        PromptTokens = e.PromptTokens,
                        CachedTokens = e.CachedTokens,
                        CompletionTokens = e.CompletionTokens,
                    };
                }
            }
        }
        catch
        {
            // Битый файл — молча начинаем срез заново.
        }
    }

    /// <summary>
    /// Прайс-таблица из строки cvar'а: "модель=вход/выход[/кэш];..." ($ за 1М токенов).
    /// Кэш-цена не указана — половина входной.
    /// </summary>
    public static Dictionary<string, (double In, double Out, double Cached)> ParsePrices(string raw)
    {
        var prices = new Dictionary<string, (double, double, double)>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.LastIndexOf('=');
            if (eq <= 0)
                continue;
            var model = part[..eq].Trim();
            var nums = part[(eq + 1)..].Split('/');
            if (nums.Length < 2
                || !double.TryParse(nums[0], System.Globalization.CultureInfo.InvariantCulture, out var input)
                || !double.TryParse(nums[1], System.Globalization.CultureInfo.InvariantCulture, out var output))
                continue;
            var cached = nums.Length >= 3
                && double.TryParse(nums[2], System.Globalization.CultureInfo.InvariantCulture, out var c)
                ? c
                : input / 2;
            prices[model] = (input, output, cached);
        }
        return prices;
    }

    /// <summary>Стоимость записи по прайсу; null = модели нет в таблице.</summary>
    private static double? CostUsd(Entry e, string model,
        Dictionary<string, (double In, double Out, double Cached)> prices)
    {
        if (!prices.TryGetValue(model, out var p))
            return null;
        var freshPrompt = Math.Max(0, e.PromptTokens - e.CachedTokens);
        return (freshPrompt * p.In + e.CachedTokens * p.Cached + e.CompletionTokens * p.Out) / 1_000_000.0;
    }

    /// <summary>Суммарная стоимость за раунд (модели вне прайса считаются за 0 — потолок мягче, не жёстче).</summary>
    public double TotalCostUsd(Dictionary<string, (double In, double Out, double Cached)> prices)
    {
        lock (_lock)
            return _entries.Sum(kv => CostUsd(kv.Value, kv.Key.Model, prices) ?? 0);
    }

    /// <summary>Человекочитаемый отчёт для консоли/лога: секция раунда + общий срез за всё время.</summary>
    public string Report(Dictionary<string, (double In, double Out, double Cached)> prices, float budgetUsd)
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            AppendSection(sb, "LLM-расход за раунд", _entries, prices,
                budgetUsd > 0 ? $" (потолок ${budgetUsd:0.##} за раунд)" : "");
            sb.AppendLine();
            AppendSection(sb, "Общий срез (за всё время, переживает рестарты)", _lifetime, prices, "");
            return sb.ToString().TrimEnd();
        }
    }

    private static void AppendSection(StringBuilder sb, string title,
        Dictionary<(string Npc, string Key, string Model), Entry> entries,
        Dictionary<string, (double In, double Out, double Cached)> prices, string suffix)
    {
        if (entries.Count == 0)
        {
            sb.AppendLine($"{title}: запросов не было.");
            return;
        }

        sb.AppendLine($"{title}:");
        var totalRequests = 0;
        var totalCost = 0.0;
        var unknownPrice = false;

        foreach (var ((npc, key, model), e) in entries.OrderBy(kv => kv.Key.Npc))
        {
            var cost = CostUsd(e, model, prices);
            totalRequests += e.Requests;
            totalCost += cost ?? 0;
            if (cost == null)
                unknownPrice = true;

            sb.AppendLine($"  {npc} | …{key} | {model}: {e.Requests} зпр, " +
                $"{K(e.PromptTokens)} промпт ({K(e.CachedTokens)} кэш), {K(e.CompletionTokens)} ответ" +
                $" ≈ {(cost is { } c ? $"${c:0.####}" : "$? (модели нет в llm_npc_prices)")}");
        }

        sb.AppendLine($"  итого: {totalRequests} запросов ≈ ${totalCost:0.####}{(unknownPrice ? "+?" : "")}{suffix}");
    }

    private static string K(long tokens)
        => tokens >= 1000 ? $"{tokens / 1000.0:0.#}k" : tokens.ToString();
}
