using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Кнопка входа на арену с хаба ротации. При нажатии игроком (<c>ActivateInWorldEvent</c>)
/// собирает всех игроков на гриде кнопки (хабе), телепортирует их на спавн-маркеры арены
/// с индексом <see cref="ArenaIndex"/> (в списке <c>arenas</c> контроллера) и запускает там раунд.
///
/// На хаб ставится по одной кнопке на каждую арену (с разными ArenaIndex). Контроллер
/// <see cref="DuelRotationComponent"/> должен быть на той же карте (он держит список арен и их
/// загруженные MapId).
/// </summary>
[RegisterComponent]
public sealed partial class DuelArenaEntryComponent : Component
{
    /// <summary>
    /// Индекс арены в списке <see cref="DuelRotationComponent.Arenas"/>, на которую ведёт кнопка.
    /// </summary>
    [DataField]
    public int ArenaIndex;

    /// <summary>
    /// Если задан — кнопка персональная: переносит ТОЛЬКО нажавшего на спавн-маркер арены с этим
    /// номером (<see cref="DuelArenaSpawnComponent.SpawnIndex"/>). Так на хабе ставят по кнопке на
    /// каждое место спавна («Спавн 1», «Спавн 2», …) и каждый боец выбирает свой угол сам.
    ///
    /// Если null — старое поведение: кнопка собирает всех мобов на гриде хаба и раскидывает их по
    /// спавн-маркерам арены по кругу.
    /// </summary>
    [DataField]
    public int? SpawnIndex;

    /// <summary>
    /// Тиры арсенал-ящиков, предлагаемые в окне выбора при нажатии кнопки. Тот же список, что у пульта
    /// (<c>ArenaArsenalRemoteComponent.Crates</c>). Выбранный тир применяется ко ВСЕМ аренам ротации.
    /// </summary>
    [DataField]
    public List<EntProtoId> Crates = new();
}
