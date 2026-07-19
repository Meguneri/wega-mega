using Robust.Shared.Audio;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Статистик арены БЕЗ нейросети: по клику (взаимодействию рукой) достаёт анализатор, стучит по
/// клавишам и печатает кликнувшему красивую распечатку его последней дуэли (ArenaFightLogSystem).
/// Никаких запросов к API — детерминированный «вендомат статистики» с парой дежурных фраз.
/// </summary>
[RegisterComponent]
public sealed partial class ArenaStatClerkComponent : Component
{
    /// <summary>Кулдаун на игрока (сек): защита от спама печати.</summary>
    [DataField]
    public float Cooldown = 8f;

    /// <summary>Задержка «поиска» между стуком по клавишам и выдачей распечатки (сек).</summary>
    [DataField]
    public float PrintDelay = 2.5f;

    /// <summary>Звук стука по клавишам анализатора (при запросе).</summary>
    [DataField]
    public SoundSpecifier TypingSound = new SoundPathSpecifier("/Audio/_Wega/Effects/keyboard_typing.ogg");

    /// <summary>Звук принтера в момент выдачи распечатки.</summary>
    [DataField]
    public SoundSpecifier PrintSound = new SoundPathSpecifier("/Audio/Machines/printer.ogg");

    /// <summary>Кому и когда печатали (кулдаун по игроку).</summary>
    [ViewVariables]
    public readonly Dictionary<EntityUid, TimeSpan> LastServed = new();

    /// <summary>Чей запрос сейчас «ищется» (между стуком и выдачей). null = свободен.</summary>
    [ViewVariables]
    public EntityUid? PendingUser;

    /// <summary>Когда выдать распечатку ожидающему.</summary>
    [ViewVariables]
    public TimeSpan PrintAt;
}
