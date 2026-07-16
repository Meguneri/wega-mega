using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Robust.Shared.ContentPack;

namespace Content.Server._Wega.LlmNpc;

/// <summary>
/// «Портрет стиля» NPC: сжатая манера речи, скопированная с логов реального игрока. Логи не таскаются
/// в промпт целиком (их могут быть миллионы строк и это дорого на КАЖДУЮ реплику) — один раз
/// прогоняются через модель в компактный портрет, который и подмешивается в промпт дёшево.
///
/// Файлы в data-папке сервера:
///   llm_npc/logs/&lt;name&gt;.txt   — сырые реплики игрока, которые кладёт админ (вход);
///   llm_npc/style_&lt;name&gt;.md    — готовый портрет стиля (выход, читается в промпт).
/// </summary>
public sealed class LlmPersona
{
    private readonly string _dir;

    public LlmPersona(IResourceManager resource)
    {
        _dir = Path.Combine(resource.UserData.RootDir ?? ".", "llm_npc");
    }

    /// <summary>Абсолютный путь к папке логов — показываем админу, куда класть файл.</summary>
    public string LogsDir => Path.Combine(_dir, "logs");

    private static string Safe(string name)
    {
        var s = Path.GetFileNameWithoutExtension(name);
        return string.IsNullOrWhiteSpace(s) ? "companion" : s;
    }

    private string StylePath(string name) => Path.Combine(_dir, "style_" + Safe(name) + ".md");
    private string LogPath(string logFile) => Path.Combine(LogsDir, Path.GetFileName(logFile));

    /// <summary>Готовый портрет стиля для промпта (или пустая строка, если его ещё нет).</summary>
    public async Task<string> ReadStyleAsync(string name)
    {
        var path = StylePath(name);
        if (!File.Exists(path))
            return string.Empty;
        try { return await File.ReadAllTextAsync(path); }
        catch { return string.Empty; }
    }

    /// <summary>
    /// Читает файл логов и берёт из него равномерную выборку не более <paramref name="cap"/> строк:
    /// стиль — высокочастотный сигнал, он ловится с нескольких десятков реплик, а больший объём лишь
    /// раздул бы стоимость дистилляции без выигрыша. В <paramref name="total"/> — сколько было всего.
    /// </summary>
    public string? ReadLogSample(string logFile, int cap, out int total)
    {
        total = 0;
        var path = LogPath(logFile);
        if (!File.Exists(path))
            return null;

        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch { return null; }

        var clean = lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        total = clean.Length;
        if (clean.Length == 0)
            return null;

        if (clean.Length <= cap)
            return string.Join("\n", clean);

        // Равномерная выборка cap строк по всему файлу — не только начало.
        var picked = new string[cap];
        var step = (double)clean.Length / cap;
        for (var i = 0; i < cap; i++)
            picked[i] = clean[(int)(i * step)];
        return string.Join("\n", picked);
    }

    /// <summary>Сохраняет готовый портрет стиля.</summary>
    public async Task WriteStyleAsync(string name, string portrait)
    {
        Directory.CreateDirectory(_dir);
        try { await File.WriteAllTextAsync(StylePath(name), portrait.Trim() + "\n"); }
        catch { /* не критично */ }
    }
}
