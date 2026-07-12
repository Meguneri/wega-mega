namespace Content.Server._Wega.Duel;

/// <summary>
/// Поднимается на трекере арены (<c>DuelArenaComponent</c>), когда раунд ГОТОВИТСЯ — бойцы уже
/// расставлены на спавн-маркеры, но бой ещё не начался (барьеры не упали). Момент выдачи арсенал-ящиков:
/// они должны стоять у спавнов всю подготовку, чтобы бойцы успели экипироваться до старта.
/// </summary>
[ByRefEvent]
public readonly record struct ArenaRoundPreparingEvent;
