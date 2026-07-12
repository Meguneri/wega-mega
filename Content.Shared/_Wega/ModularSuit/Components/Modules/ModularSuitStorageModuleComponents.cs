using Robust.Shared.Maths;

namespace Content.Shared.Modular.Suit;

[RegisterComponent]
public sealed partial class ModularSuitStorageModuleComponent : Component
{
    [DataField]
    public string ContainerId = "storagebase";

    /// <summary>
    /// Модуль сам ДОБАВИЛ хранилище костюму (легаси-путь: у MOD не было встроенного Storage, скопировали
    /// целиком). Сейчас у всех MOD хранилище встроено в базу, поэтому обычно этот путь не срабатывает и
    /// флаг остаётся false — тогда снятие модуля не удаляет встроенное хранилище.
    /// </summary>
    [DataField]
    public bool AddedStorage;

    /// <summary>
    /// Модуль РАСШИРИЛ сетку встроенного хранилища MOD (Large/Syndicate дают больше базовой 6×3). При
    /// снятии возвращаем прежнюю сетку из <see cref="PreviousGrid"/>, само хранилище не трогаем.
    /// </summary>
    [DataField]
    public bool UpgradedGrid;

    /// <summary>Сетка встроенного хранилища ДО расширения этим модулем — чтобы вернуть при снятии.</summary>
    [DataField]
    public List<Box2i>? PreviousGrid;
}
