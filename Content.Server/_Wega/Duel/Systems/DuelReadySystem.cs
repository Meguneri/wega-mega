using System.Linq;
using System.Numerics;
using Content.Server._Wega.Duel.Components;
using Content.Server.DeviceLinking.Systems;
using Robust.Server.Audio;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.GameObjects;

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
public sealed class DuelReadySystem : EntitySystem
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
    }

    /// <summary>
    /// Пересобирает голограммы «ГОТОВ». Над кнопкой висит голограмма, если сама кнопка ещё НЕ нажата,
    /// но готов хотя бы один соперник (нажата другая кнопка) — так дуэлянт у своей кнопки узнаёт, что
    /// первый уже подтвердил готовность. Над нажатыми кнопками и когда не готов никто — голограмм нет.
    /// </summary>
    private void RefreshReadyHolograms(DuelArenaComponent arena, List<EntityUid> buttons)
    {
        var anyReady = arena.ReadyButtons.Count > 0;
        foreach (var b in buttons)
        {
            var shouldShow = anyReady && !arena.ReadyButtons.Contains(b);
            var has = arena.ReadyHolograms.TryGetValue(b, out var holo) && Exists(holo);

            if (shouldShow && !has && Exists(b))
            {
                var xform = Transform(b);
                // Сдвигаем голограмму в сторону, КУДА СМОТРИТ кнопка — в комнату, а не жёстко вверх.
                // Раньше был фиксированный +Y: на картах, где кнопка на верхней (или боковой) стене,
                // голограмма уезжала В СТЕНУ. Настенная кнопка повёрнута «лицом» в комнату; её локальный
                // «низ» (0,-1), повёрнутый на LocalRotation, и есть направление в комнату (так же берут
                // направление «от стены в зал» стыковочные шлюзы, см. DockingSystem.Shuttle).
                var toRoom = xform.LocalRotation.RotateVec(new Vector2(0f, -0.65f));
                arena.ReadyHolograms[b] = Spawn(arena.ReadyHologram, xform.Coordinates.Offset(toRoom));
            }
            else if (!shouldShow && has)
            {
                QueueDel(holo);
                arena.ReadyHolograms.Remove(b);
            }
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
        arena.ReadyHolograms.Clear();
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
