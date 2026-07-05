namespace Content.Shared._Wega.Duel;

/// <summary>
/// Событие, которое <see cref="Content.Server._Wega.Duel.Systems.DuelRotationSystem"/>
/// рассылает на трекер арены после того, как бойцы перенесены на него в режиме ротации.
/// <see cref="Content.Server._Wega.Duel.Systems.DuelArenaSystem"/> подписывается и на
/// следующем тике вооружает раунд, когда грид/состояние бойцов уже «остыли».
/// </summary>
public sealed class RotationRoundStartEvent : EntityEventArgs
{
}
