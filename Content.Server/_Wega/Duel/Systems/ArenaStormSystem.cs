using Content.Server._Wega.Duel.Components;
using Content.Server.Chat.Managers;
using Content.Server.DeviceLinking.Systems;
using Content.Shared._Wega.Duel;
using Content.Shared.Damage.Systems;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Mobs.Systems;
using Robust.Server.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Драйвер «шторма» (battle-royale) на дуэльной арене. Висит на том же трекере, что и
/// <see cref="DuelArenaComponent"/>, и наблюдает за его <see cref="DuelArenaComponent.IsActive"/>:
/// — старт боя запускает отсчёт <see cref="ArenaStormComponent.StartDelay"/>;
/// — затем безопасная зона (круг от позиции трекера) пошагово сжимается к центру;
/// — зарегистрированные бойцы вне зоны получают периодический урон;
/// — конец боя сбрасывает шторм.
/// Центр зоны — собственная позиция трекера (как у авто-дропа снабжения).
/// </summary>
public sealed partial class ArenaStormSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private DeviceLinkSystem _signalSystem = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private SharedPvsOverrideSystem _pvsOverride = default!;

    /// <summary>Сигнальный порт: отменяет сужение зоны на текущий бой.</summary>
    public const string CancelPort = "StormCancel";

    /// <summary>Сигнальный порт: (пере)запускает сужение зоны посреди боя.</summary>
    public const string StartPort = "StormStart";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ArenaStormComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ArenaStormComponent, SignalReceivedEvent>(OnSignalReceived);
    }

    private void OnInit(EntityUid uid, ArenaStormComponent comp, ComponentInit args)
    {
        _signalSystem.EnsureSinkPorts(uid, CancelPort, StartPort);
    }

    private void OnSignalReceived(EntityUid uid, ArenaStormComponent comp, ref SignalReceivedEvent args)
    {
        switch (args.Port)
        {
            case CancelPort:
                CancelStorm(uid, comp);
                break;
            case StartPort:
                // Перезапуск посреди боя: снимаем отмену и заново заводим отсчёт до наступления.
                if (!TryComp<DuelArenaComponent>(uid, out var arena) || !arena.IsActive)
                    break;
                OnDuelStarted(uid, comp);
                break;
        }
    }

    /// <summary>
    /// Отменяет сужение зоны до конца текущего боя: гасит активный шторм, снимает запланированный
    /// старт и помечает раунд как отменённый, чтобы драйвер его больше не заводил.
    /// </summary>
    public void CancelStorm(EntityUid uid, ArenaStormComponent comp)
    {
        comp.Cancelled = true;
        comp.Active = false;
        comp.StartAt = null;
        Dirty(uid, comp);
        _pvsOverride.RemoveGlobalOverride(uid);

        if (comp.Announce)
            _chatManager.DispatchServerAnnouncement(
                Loc.GetString("arena-storm-cancelled"), Color.Gray);
    }

    /// <summary>
    /// Отменяет сужение зоны на всех аренах со штормом. Возвращает число затронутых арен.
    /// Вызывается консольной командой <c>arenastorm off</c>.
    /// </summary>
    public int CancelAllStorms()
    {
        var count = 0;
        var query = EntityQueryEnumerator<ArenaStormComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Cancelled && !comp.Active && comp.StartAt == null)
                continue;

            CancelStorm(uid, comp);
            count++;
        }
        return count;
    }

    /// <summary>
    /// (Пере)запускает сужение зоны на всех аренах, где сейчас идёт бой. Возвращает число затронутых
    /// арен. Вызывается консольной командой <c>arenastorm on</c>.
    /// </summary>
    public int StartAllStorms()
    {
        var count = 0;
        var query = EntityQueryEnumerator<ArenaStormComponent, DuelArenaComponent>();
        while (query.MoveNext(out var uid, out var comp, out var arena))
        {
            if (!comp.Enabled || !arena.IsActive)
                continue;

            OnDuelStarted(uid, comp);
            count++;
        }
        return count;
    }

    /// <summary>
    /// Включает или выключает шторм на конкретной арене. При включении во время боя сбрасывает отсчёт.
    /// </summary>
    public void ToggleStorm(EntityUid uid, bool enabled)
    {
        if (!TryComp<ArenaStormComponent>(uid, out var storm))
            return;

        if (enabled)
        {
            storm.Enabled = true;
            storm.Cancelled = false;
            Dirty(uid, storm);

            if (TryComp<DuelArenaComponent>(uid, out var arena) && arena.IsActive)
                OnDuelStarted(uid, storm);
        }
        else
        {
            storm.Enabled = false;
            CancelStorm(uid, storm);
        }
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ArenaStormComponent, DuelArenaComponent>();
        while (query.MoveNext(out var uid, out var storm, out var arena))
        {
            if (!storm.Enabled)
                continue;

            // Детект фронта старта/конца боя по IsActive трекера.
            if (arena.IsActive && !storm.WasDuelActive)
                OnDuelStarted(uid, storm);
            else if (!arena.IsActive && storm.WasDuelActive)
                OnDuelEnded(uid, storm);

            storm.WasDuelActive = arena.IsActive;

            if (!arena.IsActive || storm.Cancelled)
                continue;

            // Отсчёт до начала наступления шторма.
            if (storm.StartAt is { } startAt)
            {
                if (now < startAt)
                    continue;

                storm.StartAt = null;
                storm.Active = true;
                // Непрерывное сжатие: фиксируем точку отсчёта (время + радиус). Дальше радиус
                // считается из времени и на сервере, и на клиенте — Dirty шлём только здесь, раз.
                storm.ShrinkStartTime = now;
                storm.ShrinkStartRadius = storm.InitialRadius;
                storm.NextDamageAt = now + TimeSpan.FromSeconds(storm.DamageInterval);
                Dirty(uid, storm);

                if (storm.Announce)
                    _chatManager.DispatchServerAnnouncement(
                        Loc.GetString("arena-storm-incoming"), Color.OrangeRed);

                // Звук начала сужения зоны — для каждого бойца арены.
                if (storm.StormSound != null)
                    foreach (var d in arena.Duelists)
                        if (Exists(d))
                            _audio.PlayPvs(storm.StormSound, d);
            }

            if (!storm.Active)
                continue;

            // Радиус зоны теперь непрерывен (RadiusAt по времени) — отдельный шаг сжатия не нужен.
            // Тик урона по бойцам вне зоны.
            if (now >= storm.NextDamageAt)
            {
                storm.NextDamageAt = now + TimeSpan.FromSeconds(storm.DamageInterval);
                ApplyStormDamage(uid, storm, arena);
            }
        }
    }

    private void OnDuelStarted(EntityUid uid, ArenaStormComponent storm)
    {
        storm.Active = false;
        storm.Cancelled = false;
        storm.ShrinkStartRadius = storm.InitialRadius;
        storm.ShrinkStartTime = _timing.CurTime;
        storm.StartAt = _timing.CurTime + TimeSpan.FromSeconds(storm.StartDelay);
        Dirty(uid, storm);
        _pvsOverride.AddGlobalOverride(uid);
    }

    private void OnDuelEnded(EntityUid uid, ArenaStormComponent storm)
    {
        storm.Active = false;
        storm.StartAt = null;
        Dirty(uid, storm);
        _pvsOverride.RemoveGlobalOverride(uid);
    }

    private void ApplyStormDamage(EntityUid uid, ArenaStormComponent storm, DuelArenaComponent arena)
    {
        if (storm.Damage == null)
            return;

        var center = _transform.GetMapCoordinates(uid);
        // Граница урона = текущий непрерывный радиус (та же формула, что у клиентского оверлея).
        var radius = storm.RadiusAt(_timing.CurTime);
        var radiusSq = radius * radius;

        foreach (var duelist in arena.Duelists)
        {
            if (!Exists(duelist) || _mobState.IsDead(duelist))
                continue;

            var pos = _transform.GetMapCoordinates(duelist);
            if (pos.MapId != center.MapId)
                continue;

            if ((pos.Position - center.Position).LengthSquared() <= radiusSq)
                continue;

            _damageable.TryChangeDamage(duelist, storm.Damage, ignoreResistances: true, origin: uid);
        }
    }
}
