using System.IO;
using System.Threading.Tasks;
using Robust.Shared.ContentPack;

namespace Content.Server._Wega.LlmNpc;

/// <summary>
/// Файловая память NPC в data-папке сервера: llm_npc/&lt;name&gt;.md. Читается в промпт, дописывается
/// строкой на каждый факт из ответа модели. Не веса — обычный текст, поэтому «воспитание» персонажа
/// = правка/накопление этого файла, переживает рестарты.
/// </summary>
public sealed class LlmNpcMemory
{
    private readonly string _dir;

    public LlmNpcMemory(IResourceManager resource)
    {
        _dir = Path.Combine(resource.UserData.RootDir ?? ".", "llm_npc");
    }

    private string PathFor(string name)
    {
        // Защита от выхода из папки: берём только имя файла.
        var safe = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(safe))
            safe = "companion";
        return Path.Combine(_dir, safe + ".md");
    }

    /// <summary>Возвращает всю накопленную память (или пустую строку, если файла ещё нет).</summary>
    public async Task<string> ReadAsync(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
            return string.Empty;

        try
        {
            return await File.ReadAllTextAsync(path);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Дописывает одну строку-факт в память (с датой из движка — передаётся вызывающим).</summary>
    public async Task AppendAsync(string name, string stamp, string fact)
    {
        fact = fact.Trim();
        if (fact.Length == 0)
            return;

        var path = PathFor(name);
        Directory.CreateDirectory(_dir);
        try
        {
            // Пропускаем точный повтор: модель нередко пересказывает уже известное, и без этого
            // файл памяти распухает дублями, а потом весь уходит обратно в промпт.
            if (File.Exists(path))
            {
                var existing = await File.ReadAllTextAsync(path);
                if (existing.Contains(fact, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            await File.AppendAllTextAsync(path, $"- [{stamp}] {fact}\n");
        }
        catch
        {
            // Память — не критичный путь: молча пропускаем ошибку записи.
        }
    }

    /// <summary>Сколько строк-фактов накопилось (для решения о консолидации).</summary>
    public async Task<int> CountLinesAsync(string name)
    {
        var text = await ReadAsync(name);
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>Полностью переписывает память (результатом консолидации).</summary>
    public async Task RewriteAsync(string name, string content)
    {
        var path = PathFor(name);
        Directory.CreateDirectory(_dir);
        try
        {
            await File.WriteAllTextAsync(path, content.TrimEnd() + "\n");
        }
        catch
        {
            // Не критично: в худшем случае память останется несжатой до следующего раза.
        }
    }
}
