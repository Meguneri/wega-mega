using System.Linq;
using System.Numerics;
using Content.Server._Wega.Duel.Components;
using Content.Server.DeviceLinking.Systems;
using Robust.Server.Audio;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Ready-check кнопки дуэли. У каждой базы — своя кнопка; готовность считается ПО КНОПКАМ арены, а не
/// по бойцам «в радиусе» (дуэлянты сидят в отдельных запечатанных базах и в момент нажатия второй мог
/// быть ещё не виден трекеру — счёт по кнопкам от их позиций не зависит). Первое нажатие помечает
/// кнопку готовой и вешает голограмму «ГОТОВ» над кнопками ОСТАЛЬНЫХ, ещё не готовых бойцов — так
/// второй дуэлянт у своей кнопки видит, что первый уже нажал. Повторное нажатие снимает готовность.
/// Когда нажаты все кнопки арены (минимум две), кнопка программно шлёт DuelStart и запускается штатная
/// цепочка старта (таймер → DuelFight → барьеры/колокол/ArmDuel). Кнопка находит «свою» арену по гриду.
/// </summary>
public sealed partial class DuelReadySystem : EntitySystem
{
    [Dependency] private DeviceLinkSystem _signalSystem = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private AudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DuelReadyButtonComponent, ComponentInit>(OnReadyButtonInit);
        SubscribeLocalEvent<DuelReadyButtonComponent, ActivateInWorldEvent>(OnReadyButtonActivated);
    }

    // Гарантируем регистрацию порта старта до того, как AutoLink (на MapInit) свяжет кнопку с
    // таймером — иначе программный InvokePort(StartPort) не дошёл бы до таймера. YAML ports: [Pressed]
    // обычно уже достаточно; это страховка в духе EnsureSourcePorts на трекере.
    private void OnReadyButtonInit(EntityUid uid, DuelReadyButtonComponent comp, ComponentInit args)
    {
        _signalSystem.EnsureSourcePorts(uid, comp.StartPort);
    }

    private void OnReadyButtonActivated(EntityUid uid, DuelReadyButtonComponent comp, ActivateInWorldEvent args)
    {
        // args.Complex — осознанное «использование» (E/клик), а не побочное взаимодие.
        if (args.Handled || !args.Complex)
            return;

        if (!TryGetArenaForGrid(Transform(uid).GridUid, out var arenaUid, out var arena))
            return;

        args.Handled = true;

        // Во время боя кнопка не работает — готовность собирается только до старта.
        if (arena.IsActive)
        {
            _popup.PopupEntity(Loc.GetString("duel-ready-already-active"), args.User, args.User);
            return;
        }

        // Все кнопки готовности этой арены (по одной на базу) и чистка тех, что уже разрушены.
        var buttons = GetArenaButtons(arenaUid);
        PruneReadyButtons(arena, buttons);

        // Тоггл готовности нажатой кнопки.
        if (arena.ReadyButtons.Contains(uid))
            arena.ReadyButtons.Remove(uid);
        else
            arena.ReadyButtons.Add(uid);

        var isReady = arena.ReadyButtons.Contains(uid);

        // Перевешиваем голограммы «ГОТОВ» над кнопками ещё не готовых бойцов (см. RefreshReadyHolograms).
        RefreshReadyHolograms(arena, buttons);

        var msg = Loc.GetString(
            isReady ? "duel-ready-fighter-ready" : "duel-ready-fighter-unready",
            ("name", SafeName(args.User)), ("count", arena.ReadyButtons.Count), ("total", buttons.Count));

        // Объявление-индикатор видно всем рядом с кнопкой; основной индикатор — голограмма над кнопкой соперника.
        _popup.PopupCoordinates(msg, Transform(uid).Coordinates, PopupType.Medium);
        if (arena.ReadySound != null)
            _audio.PlayPvs(arena.ReadySound, uid);

        // Старт, когда нажаты все кнопки арены (минимум две).
        if (buttons.Count >= 2 && buttons.All(arena.ReadyButtons.Contains))
        {
            ClearReady(arena);
            // Программно дёргаем порт кнопки — AutoLink доставит DuelStart таймеру, дальше штатно.
            _signalSystem.InvokePort(uid, comp.StartPort);
        }
    }

    /// <summary>Все кнопки готовности на гриде арены (обычно по одной на базу).</summary>
    private List<EntityUid> GetArenaButtons(EntityUid arenaUid)
    {
        var grid = Transform(arenaUid).GridUid;
        var result = new List<EntityUid>();
        var query = EntityQueryEnumerator<DuelReadyButtonComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            // Трекер на гриде — берём кнопки только этого грида; трекер в космосе — берём все.
            if (grid == null || Transform(uid).GridUid == grid)
                result.Add(uid);
        }
        return result;
    }

    /// <summary>Снимает готовность и убирает голограммы с кнопок, которых уже нет среди кнопок арены (разрушены).</summary>
    private void PruneReadyButtons(DuelArenaComponent arena, List<EntityUid> buttons)
    {
        foreach (var b in arena.ReadyButtons.ToList())
        {
            if (!buttons.Contains(b))
                arena.ReadyButtons.Remove(b);
        }

        foreach (var b in arena.ReadyHolograms.Keys.ToList())
        {
            if (!buttons.Contains(b) && arena.ReadyHolograms.Remove(b, out var holo) && Exists(holo))
                QueueDel(holo);
        }

        foreach (var b in arena.WaitingHolograms.Keys.ToList())
        {
            if (!buttons.Contains(b) && arena.WaitingHolograms.Remove(b, out var holo) && Exists(holo))
                QueueDel(holo);
        }
    }

    /// <summary>
    /// Пересобирает голограммы над кнопками. Над кнопкой бойца, который УЖЕ нажал готовность, висит
    /// оранжевое «ОЖИДАНИЕ» (подтверждение своего нажатия, ждём соперника). Над ещё НЕ нажатой кнопкой —
    /// зелёное «ГОТОВ», если готов хотя бы один соперник (так дуэлянт у своей кнопки узнаёт, что первый
    /// уже подтвердил). Когда не готов никто — голограмм нет. На одну кнопку — не более одной голограммы.
    /// </summary>
    private void RefreshReadyHolograms(DuelArenaComponent arena, List<EntityUid> buttons)
    {
        var anyReady = arena.ReadyButtons.Count > 0;
        foreach (var b in buttons)
        {
            var isReady = arena.ReadyButtons.Contains(b);
            // «ОЖИДАНИЕ» — над кнопкой самого нажавшего; «ГОТОВ» — над кнопками ещё не готовых соперников.
            EnsureHologram(arena.WaitingHolograms, arena.WaitingHologram, b, isReady);
            EnsureHologram(arena.ReadyHolograms, arena.ReadyHologram, b, anyReady && !isReady);
        }
    }

    /// <summary>
    /// Держит ровно одну голограмму <paramref name="proto"/> над кнопкой <paramref name="button"/> в
    /// словаре <paramref name="dict"/>: спавнит, если нужна и её нет, и убирает, если больше не нужна
    /// (или уже уничтожена извне).
    /// </summary>
    private void EnsureHologram(Dictionary<EntityUid, EntityUid> dict, EntProtoId proto, EntityUid button, bool show)
    {
        var has = dict.TryGetValue(button, out var holo) && Exists(holo);

        if (show && !has && Exists(button))
        {
            var xform = Transform(button);
            // Сдвигаем голограмму в сторону, КУДА СМОТРИТ кнопка — в комнату, а не жёстко вверх.
            // Раньше был фиксированный +Y: на картах, где кнопка на верхней (или боковой) стене,
            // голограмма уезжала В СТЕНУ. Настенная кнопка повёрнута «лицом» в комнату; её локальный
            // «низ» (0,-1), повёрнутый на LocalRotation, и есть направление в комнату (так же берут
            // направление «от стены в зал» стыковочные шлюзы, см. DockingSystem.Shuttle).
            var toRoom = xform.LocalRotation.RotateVec(new Vector2(0f, -0.65f));
            dict[button] = Spawn(proto, xform.Coordinates.Offset(toRoom));
        }
        else if (!show && dict.ContainsKey(button))
        {
            if (has)
                QueueDel(holo);
            dict.Remove(button);
        }
    }

    /// <summary>Сбрасывает готовность и убирает все голограммы. Вызывается при старте/завершении/сбросе боя.</summary>
    public void ClearReady(DuelArenaComponent arena)
    {
        foreach (var holo in arena.ReadyHolograms.Values)
        {
            if (Exists(holo))
                QueueDel(holo);
        }
        foreach (var holo in arena.WaitingHolograms.Values)
        {
            if (Exists(holo))
                QueueDel(holo);
        }
        arena.ReadyHolograms.Clear();
        arena.WaitingHolograms.Clear();
        arena.ReadyButtons.Clear();
    }

    /// <summary>Находит арену по гриду кнопки. Публичный, т.к. может понадобиться другим системам арены.</summary>
    public bool TryGetArenaForGrid(EntityUid? grid, out EntityUid arenaUid, out DuelArenaComponent arena)
    {
        arenaUid = default;
        arena = default!;
        if (grid == null)
            return false;

        var query = EntityQueryEnumerator<DuelArenaComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (Transform(uid).GridUid != grid)
                continue;
            arenaUid = uid;
            arena = comp;
            return true;
        }
        return false;
    }

    private string SafeName(EntityUid uid)
        => Exists(uid) ? MetaData(uid).EntityName : "?";
}
