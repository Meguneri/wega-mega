using System.IO;
using System.Text;
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
    private static readonly HashSet<ulong> GameWordHashes = HashGameWords();
    private static readonly HashSet<string> IndefiniteBases = new(StringComparer.Ordinal)
    {
        "кто", "кого", "кому", "кем", "ком", "что", "чего", "чему", "чем", "чём",
        "какой", "какая", "какое", "какие", "какого", "какому", "каким", "какую",
        "какою", "каком", "каких", "какими", "чей", "чья", "чьё", "чье", "чьи", "чьего",
        "чьей", "чьему", "чьим", "чью", "чьею", "чьём", "чьем", "чьих", "чьими",
        "где", "куда", "откуда", "когда", "как", "почему", "зачем", "сколько", "скольких",
        "скольким", "сколькими"
    };

    private static HashSet<ulong> HashGameWords()
    {
        var hashes = new HashSet<ulong>();
        foreach (var word in GameWords)
            hashes.Add(Fingerprint(word));
        return hashes;
    }

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
            if (IsSpellingMark(character))
                continue;
            var lower = char.ToLowerInvariant(character);
            if (IsWordHyphen(lower))
                lower = '-';
            hash = unchecked((hash ^ (lower == 'ё' ? 'е' : lower)) * 1099511628211UL);
        }
        return hash;
    }

    public bool IsKnown(string word)
    {
        if (ContainsWord(word))
            return true;

        // Проверяем продуктивные формы, которых может не быть в перечне словоформ.
        // Не принимаем произвольное соединение двух правильных слов через дефис.
        var normalized = new StringBuilder(word.Length);
        foreach (var character in word)
        {
            if (!IsSpellingMark(character))
                normalized.Append(IsWordHyphen(character) ? '-' : char.ToLowerInvariant(character));
        }
        var text = normalized.ToString();
        var hyphen = text.IndexOf('-');
        if (hyphen <= 0 || hyphen == text.Length - 1 || text.IndexOf('-', hyphen + 1) >= 0)
            return false;

        var first = text.Substring(0, hyphen);
        var second = text.Substring(hyphen + 1);
        if (second == "то")
            return ContainsWord(first);
        if (second is "либо" or "нибудь")
            return IndefiniteBases.Contains(first) && ContainsWord(first);
        return first == "кое" && IndefiniteBases.Contains(second) && ContainsWord(second);
    }

    private bool ContainsWord(string word)
    {
        var value = Fingerprint(word);
        if (GameWordHashes.Contains(value))
            return true;
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

    public List<(int Start, int End)> FindErrors(string text)
    {
        var errors = new List<(int, int)>();
        if (text.TrimStart().StartsWith('/'))
            return errors;

        var tokenEnd = 0;
        var skipToken = false;
        for (var start = 0; start < text.Length;)
        {
            if (!char.IsLetterOrDigit(text[start]))
            {
                start++;
                continue;
            }

            // URL, адрес почты и упоминание проверяются целиком, включая параметры ссылки.
            if (start >= tokenEnd)
            {
                tokenEnd = start;
                while (tokenEnd < text.Length && !char.IsWhiteSpace(text[tokenEnd]))
                    tokenEnd++;
                var token = text.Substring(start, tokenEnd - start);
                skipToken = token.Contains("://", StringComparison.Ordinal) ||
                            token.StartsWith("www.", StringComparison.OrdinalIgnoreCase) || token.Contains('@');
            }
            if (skipToken || (start > 0 && text[start - 1] == '@'))
            {
                start = tokenEnd;
                continue;
            }

            var end = start + 1;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || IsSpellingMark(text[end]) ||
                   (IsWordHyphen(text[end]) && end + 1 < text.Length && char.IsLetterOrDigit(text[end + 1]))))
                end++;

            var russian = true;
            for (var i = start; i < end; i++)
            {
                var c = char.ToLowerInvariant(text[i]);
                if (c != 'ё' && !IsWordHyphen(c) && !IsSpellingMark(c) && (c < 'а' || c > 'я'))
                    russian = false;
            }

            // Не подсвечиваем имена, аббревиатуры и части ссылок; набираемое слово проверяем сразу.
            var hasLowercase = false;
            for (var i = start; i < end; i++)
                hasLowercase |= char.IsLower(text[i]);
            var name = char.IsUpper(text[start]) && hasLowercase && !IsSentenceStart(text, start);
            var abbreviation = end - start <= 5;
            for (var i = start; i < end && abbreviation; i++)
                abbreviation = char.IsUpper(text[i]);
            var link = start > 0 && (text[start - 1] == '@' || text[start - 1] == '/' || text[start - 1] == '.');
            if (russian && end - start > 2 && !name && !abbreviation && !link
                && !IsKnown(text.Substring(start, end - start)))
                errors.Add((start, end));
            start = end;
        }
        return errors;
    }

    private static bool IsSpellingMark(char character) => character is '\u0301' or '\u0300' or '\u00ad';

    private static bool IsWordHyphen(char character) => character is '-' or '\u2010' or '\u2011';

    private static bool IsSentenceStart(string text, int start)
    {
        for (var i = start - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(text[i]) || text[i] is '"' or '«' or '»' or '(' or ')')
                continue;
            return text[i] is '.' or '!' or '?' or '\u2026';
        }
        return true;
    }
}
