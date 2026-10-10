using System.Text.RegularExpressions;
using Content.Shared.Speech;
using Content.Shared.Speech.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Speech;

/// <summary>
/// Applies Orc word replacements and simplified conjugation to speech, leaving emotes intact.
/// Adapted from Aavikko-2.0 (MIT), see Resources/_Wega/Orc/LICENSE.txt.
/// </summary>
public sealed partial class OrcAccentSystem : EntitySystem
{
    private Dictionary<string, string>? _words;
    private static readonly ProtoId<ReplacementAccentPrototype> AccentPrototype = "orc";

    private static readonly Regex Words = new(@"\b[\p{L}\p{M}]+\b",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));

    private static readonly Regex FirstConjugation = new(@"а(?:ю|ешь|ет|ем|ете|ют)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex FirstConjugationSoft = new(@"я(?:ю|ешь|ет|ем|ете|ют)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex SecondConjugation = new(@"(?:ю|ишь|ит|им|ите|ят)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OrcAccentComponent, AccentGetEvent>(OnAccent);
        ProtoMan.PrototypesReloaded += OnPrototypesReloaded;
    }

    public override void Shutdown()
    {
        ProtoMan.PrototypesReloaded -= OnPrototypesReloaded;
        base.Shutdown();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        _words = null;
    }

    private string ReplaceWord(Match match)
    {
        if (_words == null || !_words.TryGetValue(match.Value, out var replacement))
            return match.Value;

        var allUpper = match.Length > 1;
        foreach (var letter in match.Value)
            allUpper &= char.IsUpper(letter);

        if (allUpper)
            return replacement.ToUpperInvariant();
        if (char.IsUpper(match.Value[0]))
            return replacement[0].ToString().ToUpperInvariant() + replacement.Substring(1);

        return replacement;
    }

    private void OnAccent(Entity<OrcAccentComponent> ent, ref AccentGetEvent args)
    {
        if (_words == null)
        {
            _words = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var prototype = ProtoMan.Index(AccentPrototype);
            if (prototype.WordReplacements is { } replacements)
            {
                foreach (var (word, replacement) in replacements)
                    _words.TryAdd(Loc.GetString(word), Loc.GetString(replacement));
            }
        }

        var message = args.Message;
        try
        {
            // Один проход вместо многократных Remove/Insert для тысяч повторяющихся слов.
            message = Words.Replace(message, ReplaceWord);
            message = FirstConjugation.Replace(message, "ать");
            message = FirstConjugationSoft.Replace(message, "ять");
            message = SecondConjugation.Replace(message, "ить");
        }
        catch (RegexMatchTimeoutException)
        {
            // Длинная реплика остаётся читаемой даже при исчерпании времени обработки.
        }

        args.Message = message;
    }
}
