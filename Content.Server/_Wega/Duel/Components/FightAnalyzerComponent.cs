using Robust.Shared.Audio;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Портативный боевой анализатор как используемый предмет: активация открывает UI со списком
/// дуэлей сессии (ArenaFightLogSystem) и сводкой; любую запись можно распечатать — бумага
/// падает в руки открывшему. Работает независимо от NPC (Макс носит такой же в сумке).
/// </summary>
[RegisterComponent]
public sealed partial class FightAnalyzerComponent : Component
{
    /// <summary>Звук печати при выдаче распечатки.</summary>
    [DataField]
    public SoundSpecifier PrintSound = new SoundPathSpecifier("/Audio/_Wega/Effects/keyboard_typing.ogg");

    /// <summary>Кулдаун печати, сек (защита от бумажного спама).</summary>
    [DataField]
    public float PrintCooldown = 3f;

    /// <summary>Когда можно печатать снова.</summary>
    [ViewVariables]
    public TimeSpan NextPrint;
}
