using System;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Robust.Shared.Console;

namespace Content.Server._Wega.LlmNpc;

/// <summary>
/// Дистилляция «портрета стиля» из логов реального игрока: один тяжёлый проход модели превращает
/// сырые реплики в компактную манеру речи, которую NPC потом дёшево подмешивает в промпт. Запускается
/// админ-командой; сам вызов к API — в фоне, результат и путь к файлу уходят обратно в консоль.
/// </summary>
public sealed partial class LlmNpcSystem
{
    // Сколько реплик из лога отдаём модели: стиль ловится с нескольких десятков, больше — лишние деньги.
    private const int PersonaSampleCap = 250;

    private const string DistillSystem =
        "Ты — аналитик речи. По приведённым репликам ОДНОГО человека составь компактный портрет его " +
        "манеры общения. Опиши ТОЛЬКО как он говорит, а не о чём: тон и характер, типичные словечки и " +
        "обороты, длину и ритм фраз, как реагирует на конфликт и шутки, уровень вежливости/грубости. " +
        "Не пересказывай события. 6–10 коротких пунктов, по-русски. Это будет инструкцией другому " +
        "персонажу, чтобы копировать манеру.";

    /// <summary>
    /// Запускает дистилляцию: читает llm_npc/logs/&lt;logFile&gt;, гоняет выборку через модель и пишет
    /// портрет в llm_npc/style_&lt;memoryFile&gt;.md. Всё после проверок — в фоне; отчёт уходит в shell.
    /// </summary>
    public void DistillPersona(string memoryFile, string logFile, IConsoleShell shell)
    {
        if (!_cfg.GetCVar(WegaCVars.LlmNpcEnabled))
        {
            shell.WriteError("LLM-NPC выключен (wega.llm_npc_enabled).");
            return;
        }

        var apiKey = _cfg.GetCVar(WegaCVars.LlmNpcApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            shell.WriteError("Не задан ключ API (wega.llm_npc_api_key).");
            return;
        }

        var endpoint = _cfg.GetCVar(WegaCVars.LlmNpcEndpoint);
        var model = _cfg.GetCVar(WegaCVars.LlmNpcModel);

        shell.WriteLine($"Дистилляция стиля из logs/{logFile} → style_{memoryFile}.md …");
        _ = DistillAsync(memoryFile, logFile, endpoint, apiKey, model, shell);
    }

    private async Task DistillAsync(string memoryFile, string logFile,
        string endpoint, string apiKey, string model, IConsoleShell shell)
    {
        try
        {
            var sample = _persona.ReadLogSample(logFile, PersonaSampleCap, out var total);
            if (sample == null)
            {
                Reply(shell, $"Файл логов не найден или пуст. Положи реплики игрока в: {_persona.LogsDir}/{logFile}");
                return;
            }

            var portrait = await _backend.AskRawAsync(endpoint, apiKey, model, DistillSystem, sample, _sawmill);
            if (string.IsNullOrWhiteSpace(portrait))
            {
                Reply(shell, "Модель не вернула портрет (см. лог сервера).");
                return;
            }

            await _persona.WriteStyleAsync(memoryFile, portrait);
            _sawmill.Info($"портрет стиля для {memoryFile} готов ({total} строк лога → {portrait!.Length} симв.)");
            Reply(shell,
                $"Готово: портрет стиля сохранён (обработано {total} строк, выборка {Math.Min(total, PersonaSampleCap)}). " +
                $"NPC с memoryFile «{memoryFile}» подхватит манеру со следующего ответа.\n\n{portrait}");
        }
        catch (Exception e)
        {
            _sawmill.Warning($"дистилляция стиля упала: {e.Message}");
            Reply(shell, $"Ошибка дистилляции: {e.Message}");
        }
    }

    /// <summary>Ответ в консоль админа — только на главном потоке (shell не потокобезопасен).</summary>
    private void Reply(IConsoleShell shell, string message)
    {
        _taskManager.RunOnMainThread(() =>
        {
            try { shell.WriteLine(message); }
            catch { /* админ мог отключиться — не критично */ }
        });
    }
}
