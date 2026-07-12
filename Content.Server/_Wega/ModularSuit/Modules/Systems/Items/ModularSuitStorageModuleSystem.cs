using Content.Shared.Modular.Suit;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.Maths;

namespace Content.Server.Modular.Suit;

public sealed partial class ModularSuitStorageModuleSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedStorageSystem _storage = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModularSuitStorageModuleComponent, ModularSuitInstalledEvent>(OnModuleInstalled);
        SubscribeLocalEvent<ModularSuitStorageModuleComponent, ModularSuitRemovedEvent>(OnModuleRemoved);
    }

    private void OnModuleInstalled(Entity<ModularSuitStorageModuleComponent> module, ref ModularSuitInstalledEvent args)
    {
        AddStorageToSuit(args.Suit, module);
    }

    private void OnModuleRemoved(Entity<ModularSuitStorageModuleComponent> module, ref ModularSuitRemovedEvent args)
    {
        RemoveStorageFromSuit(args.Suit, module);
    }

    private void AddStorageToSuit(EntityUid suit, Entity<ModularSuitStorageModuleComponent> module)
    {
        if (!TryComp<StorageComponent>(module.Owner, out var moduleStorage))
            return;

        // У всех MOD хранилище встроено в базу (ClothingModularControllerBase). Storage-модуль теперь не
        // добавляет новое хранилище, а РАСШИРЯЕТ сетку встроенного до своей (Large/Syndicate > базовой
        // 6×3). Прежнюю сетку запоминаем, чтобы вернуть при снятии; само хранилище и его содержимое не
        // трогаем — вещи внутри костюма остаются на месте.
        if (TryComp<StorageComponent>(suit, out var suitStorage))
        {
            module.Comp.PreviousGrid = new List<Box2i>(suitStorage.Grid);
            module.Comp.UpgradedGrid = true;
            suitStorage.Grid = new List<Box2i>(moduleStorage.Grid);
            // MaxItemSize не трогаем — у базы и всех storage-модулей он одинаковый (Huge), а прямая
            // запись MaxItemSize вне storage-системы запрещена анализатором доступа (RA0002).
            Dirty(suit, suitStorage);
            return;
        }

        // Легаси-путь: у MOD почему-то нет встроенного хранилища — копируем модульное целиком, как раньше.
        _storage.CopyComponent((module.Owner, moduleStorage), suit);
        module.Comp.AddedStorage = true;
        if (TryComp<StorageComponent>(suit, out var storage))
        {
            storage.ShowVerb = true;
            storage.ClickInsert = true;
            storage.OpenOnActivate = true;
            Dirty(suit, storage);
        }
    }

    private void RemoveStorageFromSuit(EntityUid suit, Entity<ModularSuitStorageModuleComponent> module)
    {
        if (!TryComp<StorageComponent>(suit, out var storage))
            return;

        // Модуль расширял сетку встроенного хранилища — возвращаем прежнюю, хранилище НЕ удаляем и вещи
        // не выкидываем.
        if (module.Comp.UpgradedGrid)
        {
            if (module.Comp.PreviousGrid != null)
                storage.Grid = new List<Box2i>(module.Comp.PreviousGrid);

            module.Comp.UpgradedGrid = false;
            module.Comp.PreviousGrid = null;
            Dirty(suit, storage);
            return;
        }

        // Легаси: модуль сам добавлял хранилище (у MOD не было встроенного) — выкидываем вещи и удаляем.
        if (!module.Comp.AddedStorage)
            return;

        if (storage.Container.ID != module.Comp.ContainerId)
            return;

        var coords = Transform(suit).Coordinates;
        if (TryComp<ModularSuitComponent>(suit, out var modular) && modular.Wearer != null)
            coords = Transform(modular.Wearer.Value).Coordinates;

        _container.EmptyContainer(storage.Container, true, coords);

        RemComp<StorageComponent>(suit);
    }
}
