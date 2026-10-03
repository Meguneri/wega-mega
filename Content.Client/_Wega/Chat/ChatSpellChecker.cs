using System.IO;
using Robust.Client.ResourceManagement;
using Robust.Shared.Utility;

namespace Content.Client._Wega.Chat;

/// <summary>Offline Russian word-form lookup shared by chat inputs.</summary>
public sealed class ChatSpellChecker
{
    private readonly ulong[] _words;
    private static ChatSpellChecker? _instance;
    private static readonly HashSet<string> GameWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "эмоут", "эмоуты", "эмоута", "эмоутом", "рп", "ооц", "оос", "ик", "афк",
        "сс", "сб", "смо", "кпк", "нт", "вульпканин", "вульпканина", "вульпканином",
        "ниан", "ниана", "нианом", "диона", "дионы", "слайм", "слайма", "слаймом",
        "унатх", "унатха", "унатхом", "вега", "веги", "веге", "вегой"
    };

    public ChatSpellChecker(ulong[] words)
    {
        _words = words;
    }

    public static ChatSpellChecker Get(IResourceCache resources)
    {
        if (_instance != null)
            return _instance;

        using var stream = resources.ContentFileRead(new ResPath("/_Wega/Spelling/russian.bin"));
        using var reader = new BinaryReader(stream);
        var count = reader.ReadInt32();
        var words = new ulong[count];
        for (var i = 0; i < count; i++)
            words[i] = reader.ReadUInt64();
        return _instance = new ChatSpellChecker(words);
    }

    public static ulong Fingerprint(string word)
    {
        var hash = 14695981039346656037UL;
        foreach (var character in word)
        {
            var lower = char.ToLowerInvariant(character);
            hash = unchecked((hash ^ (lower == 'ё' ? 'е' : lower)) * 1099511628211UL);
        }
        return hash;
    }

    public bool IsKnown(string word)
    {
        if (GameWords.Contains(word))
            return true;
        var value = Fingerprint(word);
        var left = 0;
        var right = _words.Length - 1;
        // Array.BinarySearch<T> не разрешён клиентской песочницей.
        while (left <= right)
        {
            var middle = left + (right - left) / 2;
            if (_words[middle] == value)
                return true;
            if (_words[middle] < value)
                left = middle + 1;
            else
                right = middle - 1;
        }
        return false;
    }

    public List<(int Start, int End)> FindErrors(string text, int cursor)
    {
        var errors = new List<(int, int)>();
        if (text.TrimStart().StartsWith('/'))
            return errors;

        for (var start = 0; start < text.Length;)
        {
            if (!char.IsLetterOrDigit(text[start]))
            {
                start++;
                continue;
            }
            var end = start + 1;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '-'))
                end++;

            var russian = true;
            for (var i = start; i < end; i++)
            {
                var c = char.ToLowerInvariant(text[i]);
                if (c != 'ё' && c != '-' && (c < 'а' || c > 'я'))
                    russian = false;
            }

            // Не подсвечиваем набираемое слово, имена, аббревиатуры и части ссылок.
            var name = char.IsUpper(text[start]) && start > 0;
            var link = start > 0 && (text[start - 1] == '@' || text[start - 1] == '/' || text[start - 1] == '.');
            if (russian && end - start > 2 && !name && !link && !(cursor > start && cursor <= end)
                && !IsKnown(text.Substring(start, end - start)))
                errors.Add((start, end));
            start = end;
        }
        return errors;
    }
}
