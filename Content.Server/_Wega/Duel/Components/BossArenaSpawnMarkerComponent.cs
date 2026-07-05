namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Маркер точки появления для босс-арены.
/// </summary>
[RegisterComponent]
public sealed partial class BossArenaSpawnMarkerComponent : Component
{
    /// <summary>
    /// Тип маркера: место появления участника или босса.
    /// </summary>
    [DataField]
    public BossArenaSpawnType SpawnType = BossArenaSpawnType.Participant;

    /// <summary>
    /// Индекс для детерминированной привязки участника. Для босса не используется.
    /// </summary>
    [DataField]
    public int Index;
}

public enum BossArenaSpawnType : byte
{
    Participant,
    Boss
}
