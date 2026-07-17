using System;
using System.Linq;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Shared.Chat;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC;
using Robust.Shared.GameObjects;

namespace Content.Server._Wega.LlmNpc;

/// <summary>
/// Тело NPC: простые поручения — подойти, отнести и вручить, поднять предмет, следовать, вернуться
/// на своё место. Ходьба идёт через штатный NPC-стиринг (pathfinding), как у арестатора: ActiveNPC +
/// NPCSteering без HTN-мозга. Поручение одно за раз; новое замещает старое.
/// </summary>
public sealed partial class LlmNpcSystem
{
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private Robust.Shared.Random.IRobustRandom _random = default!;
    [Dependency] private Content.Shared.StatusEffectNew.StatusEffectsSystem _status = default!;
    [Dependency] private Content.Shared.Damage.Systems.DamageableSystem _damageable = default!;
    [Dependency] private Content.Shared.Weapons.Melee.SharedMeleeWeaponSystem _melee = default!;

    /// <summary>Радиус, в котором NPC ищет человека/предмет по имени (шире слуха: «отнеси за стол»).</summary>
    private const float FindRange = 12f;

    /// <summary>Дистанция «дошла»: хватает, чтобы протянуть стакан через стойку или поднять предмет.</summary>
    private const float ArriveRange = 1.4f;

    /// <summary>При следовании держится чуть дальше — не топчется по пяткам.</summary>
    private const float FollowRange = 2.2f;

    /// <summary>
    /// Дотянуться через препятствие: барная стойка не проходима для NPC, поэтому «дойти вплотную»
    /// к гостю за ней нельзя (NoPath). С этого расстояния она протягивает предмет через стойку.
    /// </summary>
    private const float ReachOverRange = 2.6f;

    private static readonly TimeSpan ErrandLimit = TimeSpan.FromSeconds(30);

    /// <summary>Сколько занимает смешивание напитка у стойки.</summary>
    private static readonly TimeSpan MixTime = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Записывает собственное действие NPC в его диалоговый контекст. Свои эмоуты он не «слышит»
    /// (отфильтрованы от зацикливания), поэтому без таких заметок модель не знает, что заказ уже
    /// выполнен — и на «спасибо» делала коктейль по второму разу.
    /// </summary>
    private void NoteOwnAction(EntityUid uid, LlmNpcComponent npc, string action)
    {
        npc.Heard.Add($"{MetaData(uid).EntityName} (действие): *{action}*");
        if (npc.Heard.Count > npc.ContextLines)
            npc.Heard.RemoveRange(0, npc.Heard.Count - npc.ContextLines);
    }

    /// <summary>Гоняется из Update: ведёт активные поручения — дорога, прибытие, вручение, таймаут.</summary>
    private void UpdateErrands()
    {
        var now = _timing.RealTime;
        var query = EntityQueryEnumerator<LlmNpcComponent>();
        while (query.MoveNext(out var uid, out var npc))
        {
            // Без сознания — тело не работает: ни поручений, ни взгляда, ни блуждания.
            if (_mobState.IsIncapacitated(uid))
            {
                if (npc.Errand != LlmErrand.None)
                    CancelErrand(uid, npc);
                continue;
            }

            UpdateRegen(uid, npc, now);

            if (npc.Errand == LlmErrand.None)
            {
                UpdateGaze(uid, npc, now);
                UpdateWander(uid, npc, now);
                continue;
            }

            // Переминание — фоновая мелочь: дошли до точки или вышло время — просто стоим дальше.
            if (npc.Errand == LlmErrand.Wander)
            {
                var wanderDone = npc.WanderTo is not { } wander
                    || (_transform.GetWorldPosition(uid) - _transform.ToMapCoordinates(wander).Position).Length() <= ArriveRange
                    || now >= npc.ErrandTimeout;
                if (wanderDone)
                {
                    npc.WanderTo = null;
                    CancelErrand(uid, npc);
                }
                continue;
            }

            // Вышло время — бросаем затею и возвращаемся на место (Follow бессрочен, у него таймаута нет).
            if (npc.ErrandTimeout is { } limit && now >= limit)
            {
                // Доставка «застряла» у стойки, но гость в пределах вытянутой руки — вручаем, не бросаем.
                if (npc.Errand == LlmErrand.GiveItem && npc.ErrandTarget is { } t && Exists(t)
                    && (_transform.GetWorldPosition(uid) - _transform.GetWorldPosition(t)).Length() <= ReachOverRange)
                {
                    Arrive(uid, npc, t);
                    continue;
                }

                CancelErrand(uid, npc, goHome: npc.Errand != LlmErrand.GoHome);
                continue;
            }

            var npcPos = _transform.GetWorldPosition(uid);

            // Смешивание: стоим у бара, ждём таймер готовности.
            if (npc.Errand == LlmErrand.Mixing)
            {
                if (npc.MixUntil is { } mixUntil && now >= mixUntil)
                    FinishMixing(uid, npc);
                continue;
            }

            // Дорога к раздатчику (или к своему месту) перед смешиванием.
            if (npc.Errand == LlmErrand.GoMix)
            {
                // Пункт назначения: раздатчик, если жив, иначе своё место, иначе — мешаем где стоим.
                var destination = npc.ErrandTarget is { } disp && Exists(disp)
                    ? Transform(disp).Coordinates
                    : npc.Home;

                if (destination is not { } dest
                    || (npcPos - _transform.ToMapCoordinates(dest).Position).Length() <= ArriveRange + 0.4f)
                {
                    _steering.Unregister(uid);
                    npc.Errand = LlmErrand.Mixing;
                    npc.MixUntil = now + MixTime;
                    npc.ErrandTimeout = now + ErrandLimit;
                    if (npc.PendingDrink is { } mix)
                        _chat.TrySendInGameICMessage(uid, $"достаёт шейкер и берётся за «{mix.Name}»",
                            InGameICChatType.Emote, ChatTransmitRange.Normal);
                }
                else
                    _steering.TryRegister(uid, dest);
                continue;
            }

            // Возврат домой — цель-точка, не сущность.
            if (npc.Errand == LlmErrand.GoHome)
            {
                if (npc.Home is not { } home || !Exists(home.EntityId))
                {
                    CancelErrand(uid, npc);
                    continue;
                }

                if ((npcPos - _transform.ToMapCoordinates(home).Position).Length() <= ArriveRange)
                    CancelErrand(uid, npc);
                else
                    _steering.TryRegister(uid, home);
                continue;
            }

            // Остальные поручения — цель-сущность.
            if (npc.ErrandTarget is not { } target || !Exists(target) || TerminatingOrDeleted(target)
                || (npc.Errand == LlmErrand.GiveItem && (npc.ErrandItem is not { } item || !Exists(item))))
            {
                CancelErrand(uid, npc, goHome: npc.Errand == LlmErrand.GiveItem);
                continue;
            }

            var targetPos = _transform.GetWorldPosition(target);
            var distance = (npcPos - targetPos).Length();

            if (npc.Errand == LlmErrand.Follow)
            {
                // Бессрочно держимся рядом: далеко — идём, близко — стоим.
                if (distance > FollowRange)
                    RepathIfTargetMoved(uid, target);
                continue;
            }

            if (distance <= ArriveRange)
            {
                Arrive(uid, npc, target);
                continue;
            }

            // В пределах вытянутой руки, а ближе не подойти: пути нет (гость за барной стойкой)
            // либо стиринг уже считает цель достигнутой — действуем через стойку.
            if (distance <= ReachOverRange
                && TryComp<NPCSteeringComponent>(uid, out var steer)
                && steer.Status is SteeringStatus.NoPath or SteeringStatus.InRange)
            {
                Arrive(uid, npc, target);
                continue;
            }

            // Цель может ходить — подводим маршрут к её текущей позиции.
            RepathIfTargetMoved(uid, target);
        }
    }

    /// <summary>
    /// Перепрокладывает маршрут к цели, только если она заметно ушла от прошлой точки маршрута.
    /// Перерегистрация каждый тик (координаты идущего игрока меняются постоянно) отменяла
    /// незавершённый поиск пути снова и снова — NPC «следовал» стоя на месте.
    /// </summary>
    private void RepathIfTargetMoved(EntityUid uid, EntityUid target)
    {
        var targetCoords = Transform(target).Coordinates;

        if (TryComp<NPCSteeringComponent>(uid, out var steering))
        {
            var old = _transform.ToMapCoordinates(steering.Coordinates).Position;
            var now = _transform.ToMapCoordinates(targetCoords).Position;
            // Порог — как RepathRange у стиринга: цель в пределах шага от старого маршрута — не трогаем.
            if ((old - now).Length() < 1.5f)
                return;
        }

        // Через RegisterSteering: сбрасывает застрявший статус InRange/NoPath (см. комментарий там).
        RegisterSteering(uid, targetCoords);
    }

    /// <summary>
    /// «Живой взгляд»: стоя без дела, NPC поворачивается к ближайшему игроку рядом — следит за ним,
    /// а не пялится в стену, пока с ним не заговорят. Троттлинг раз в полсекунды, только настоящие
    /// игроки (ActorComponent) в радиусе слуха.
    /// </summary>
    private void UpdateGaze(EntityUid uid, LlmNpcComponent npc, TimeSpan now)
    {
        if (now < npc.NextGaze)
            return;
        npc.NextGaze = now + TimeSpan.FromSeconds(0.5);

        var map = Transform(uid).MapID;
        var origin = _transform.GetWorldPosition(uid);

        EntityUid? nearest = null;
        var nearestDist = npc.HearingRange;
        var query = EntityQueryEnumerator<Robust.Shared.Player.ActorComponent, MobStateComponent>();
        while (query.MoveNext(out var mob, out _, out _))
        {
            if (mob == uid || Transform(mob).MapID != map)
                continue;
            var dist = (_transform.GetWorldPosition(mob) - origin).Length();
            if (dist < nearestDist)
            {
                nearest = mob;
                nearestDist = dist;
            }
        }

        if (nearest is { } target)
        {
            _rotateToFace.TryFaceCoordinates(uid, _transform.GetWorldPosition(target));

            // В мьюте (be_quiet) проактивность выключена — молчать значит молчать.
            if (npc.MuteUntil is { } mute && now < mute)
                return;

            // Проактивность 1: новый гость подошёл близко — поздороваться первой.
            // Кулдаун на человека, чтобы не приветствовать каждые полминуты.
            if (nearestDist <= 4f
                && (!npc.Greeted.TryGetValue(target, out var last) || now - last > TimeSpan.FromMinutes(5)))
            {
                npc.Greeted[target] = now;
                NoteOwnAction(uid, npc, $"замечает, что к бару подошёл {MetaData(target).EntityName} — стоит поприветствовать");
                npc.ReplyAt = now + TimeSpan.FromSeconds(1);
            }
            // Проактивность 2: гость рядом, но повисла долгая тишина — сама подкинуть тему.
            else if (now - npc.LastHeardAt > TimeSpan.FromSeconds(90) && now >= npc.NextNudge
                && npc.LastHeardAt != TimeSpan.Zero)
            {
                npc.NextNudge = now + TimeSpan.FromMinutes(4);
                // Разные поводы заговорить — и ни один не про «предложить напиток»: карусель
                // «какао-печеньки-глинтвейн» всех достала. Можно и просто мысль вслух, ни к кому.
                var idleIdeas = new[]
                {
                    "тишина — можно подумать вслух о чём-то своём: о станции, погоде за иллюминатором, странном сне (ни к кому не обращаясь)",
                    "тишина — можно прокомментировать обстановку вокруг: что видишь, что изменилось, кто чем занят",
                    "тишина — можно вспомнить занятный факт или историю (хоть из web_search) и поделиться просто так",
                    "тишина — можно спросить гостя о чём-то новом, чего ещё НЕ спрашивала (не о напитках!)",
                    "тишина — можно тихо позаниматься своим делом и бросить одну фразу-наблюдение",
                };
                NoteOwnAction(uid, npc, idleIdeas[_random.Next(idleIdeas.Length)]);
                npc.ReplyAt = now + TimeSpan.FromSeconds(1);
            }
        }
    }

    /// <summary>
    /// «Жизнь в простое»: раз в 15–45 секунд шагнуть на соседний тайл у своего места и вернуться —
    /// протереть стойку, переставить бутылку. NPC перестаёт выглядеть статуей. Далеко от дома не
    /// уходим: блуждание всегда целится в тайл рядом с Home, так что она же и «возвращается на место».
    /// </summary>
    private void UpdateWander(EntityUid uid, LlmNpcComponent npc, TimeSpan now)
    {
        if (npc.Home is not { } home || now < npc.NextWander)
            return;

        // Идёт разговор (речь < 30 сек назад) и гость рядом — стоим и слушаем, не расхаживаем
        // перед носом у собеседника.
        if (now - npc.LastHeardAt < TimeSpan.FromSeconds(30) && npc.LastHeardAt != TimeSpan.Zero)
        {
            npc.NextWander = now + TimeSpan.FromSeconds(15);
            return;
        }

        npc.NextWander = now + TimeSpan.FromSeconds(_random.NextFloat(10f, 35f));

        // Целевая точка: до двух тайлов от «своего места» в любую сторону — прогуляться вдоль
        // стойки, переставить бутылку, глянуть в иллюминатор. Радиус мал, так что она всегда
        // остаётся «у себя»; сам якорь двигается только командами (стой тут / walk / go_to).
        var dx = _random.Next(-2, 3);
        var dy = _random.Next(-2, 3);
        if (dx == 0 && dy == 0)
            dy = 1;
        var target = home.Offset(new System.Numerics.Vector2(dx, dy));

        npc.Errand = LlmErrand.Wander;
        npc.WanderTo = target;
        npc.ErrandTimeout = now + TimeSpan.FromSeconds(8);
        EnsureComp<ActiveNPCComponent>(uid);
        RegisterSteering(uid, target).Range = ArriveRange - 0.2f;
    }

    /// <summary>Паника: бежим от обидчика — точка в противоположную сторону, ~9 тайлов.</summary>
    private void FleeFrom(EntityUid uid, EntityUid attacker)
    {
        if (!TryComp<LlmNpcComponent>(uid, out var npc))
            return;

        var away = _transform.GetWorldPosition(uid) - _transform.GetWorldPosition(attacker);
        if (away.LengthSquared() < 0.01f)
            away = new System.Numerics.Vector2(1, 0);
        away = System.Numerics.Vector2.Normalize(away) * 9f;

        CancelErrand(uid, npc);
        npc.Errand = LlmErrand.Wander;
        npc.WanderTo = Transform(uid).Coordinates.Offset(away);
        npc.ErrandTimeout = _timing.RealTime + TimeSpan.FromSeconds(15);
        EnsureComp<ActiveNPCComponent>(uid);
        RegisterSteering(uid, npc.WanderTo.Value).Range = ArriveRange;
    }

    /// <summary>
    /// «Отлежаться»: медленное самолечение лёгких ран, только вне боя (10с без урона). Не магия
    /// бессмертия — просто чтобы после стычки она не ковыляла побитой весь раунд без медиков.
    /// </summary>
    private void UpdateRegen(EntityUid uid, LlmNpcComponent npc, TimeSpan now)
    {
        if (now < npc.NextRegen || now - npc.LastHurtAt < TimeSpan.FromSeconds(10))
            return;
        npc.NextRegen = now + TimeSpan.FromSeconds(2);

#pragma warning disable CS0618
        if (_damageable.GetTotalDamage(uid) <= 0)
            return;
#pragma warning restore CS0618

        var heal = new Content.Shared.Damage.DamageSpecifier();
        foreach (var type in new[] { "Blunt", "Slash", "Piercing", "Heat" })
            heal.DamageDict.Add(type, -1.5);
        _damageable.TryChangeDamage(uid, heal, ignoreResistances: true);
    }

    /// <summary>Дошли до цели: завершаем поручение по его типу.</summary>
    private void Arrive(EntityUid uid, LlmNpcComponent npc, EntityUid target)
    {
        switch (npc.Errand)
        {
            case LlmErrand.GiveItem when npc.ErrandItem is { } item && Exists(item):
            {
                var itemName = MetaData(item).EntityName;
                var targetName = MetaData(target).EntityName;

                _rotateToFace.TryFaceCoordinates(uid, _transform.GetWorldPosition(target));

                // Сначала выпустить из своих рук, иначе предмет «в контейнере» и не переложится.
                _hands.TryDrop((uid, null), item, checkActionBlocker: false);
                _hands.PickupOrDrop(target, item, checkActionBlocker: false, dropNear: true);

                _chat.TrySendInGameICMessage(uid, $"протягивает {itemName} — {targetName}",
                    InGameICChatType.Emote, ChatTransmitRange.Normal);
                NoteOwnAction(uid, npc, $"вручила {itemName} — {targetName}. Заказ ВЫПОЛНЕН, повторять не нужно");
                _sawmill.Info($"{ToPrettyString(uid)} вручила {itemName} → {targetName}");

                // Доставила — и обратно за стойку.
                CancelErrand(uid, npc, goHome: true);
                return;
            }

            case LlmErrand.PickUp:
            {
                var itemName = MetaData(target).EntityName;
                if (_hands.TryPickupAnyHand(uid, target, checkActionBlocker: false))
                {
                    _chat.TrySendInGameICMessage(uid, $"поднимает {itemName}",
                        InGameICChatType.Emote, ChatTransmitRange.Normal);
                    _sawmill.Info($"{ToPrettyString(uid)} подняла {itemName}");
                }
                break;
            }
        }

        // GoTo: пришла по команде — теперь её место ЗДЕСЬ (не утаскивать блужданием обратно в бар).
        if (npc.Errand == LlmErrand.GoTo)
            npc.Home = Transform(uid).Coordinates;

        // GoTo/PickUp: остаёмся на месте — дальше распорядится голова.
        CancelErrand(uid, npc);
    }

    private void CancelErrand(EntityUid uid, LlmNpcComponent npc, bool goHome = false)
    {
        npc.Errand = LlmErrand.None;
        npc.ErrandTarget = null;
        npc.ErrandItem = null;
        npc.ErrandTimeout = null;
        npc.PendingDrink = null;
        npc.PendingServe = null;
        npc.MixUntil = null;
        npc.WanderTo = null;

        if (!Exists(uid))
            return;

        _steering.Unregister(uid);

        if (goHome && npc.Home != null)
        {
            npc.Errand = LlmErrand.GoHome;
            npc.ErrandTimeout = _timing.RealTime + ErrandLimit;
            RegisterSteering(uid, npc.Home.Value).Range = ArriveRange - 0.2f;
            EnsureComp<ActiveNPCComponent>(uid);
        }
    }

    private void BeginErrand(EntityUid uid, LlmNpcComponent npc, LlmErrand errand, EntityUid target,
        EntityUid? item = null)
    {
        npc.Errand = errand;
        npc.ErrandTarget = target;
        npc.ErrandItem = item;
        // Follow бессрочный — прекращается только по слову или пропаже цели.
        npc.ErrandTimeout = errand == LlmErrand.Follow ? null : _timing.RealTime + ErrandLimit;

        // Без ActiveNPC система стиринга не двигает сущность, а HTN-мозга у компаньона нет.
        EnsureComp<ActiveNPCComponent>(uid);
        var steering = RegisterSteering(uid, Transform(target).Coordinates);
        // Чуть ближе, чем «дошла», чтобы стиринг не бросал раньше; у Follow — своя дистанция.
        // Вручение — с запасом на стойку: гость за барной стойкой недостижим вплотную (NoPath),
        // но тайл на нашей стороне в паре шагов от него есть — до него маршрут строится.
        steering.Range = errand switch
        {
            LlmErrand.Follow => FollowRange - 0.4f,
            LlmErrand.GiveItem => ReachOverRange - 0.3f,
            _ => ArriveRange - 0.2f,
        };
    }

    /// <summary>
    /// Регистрация маршрута с правом открывать двери. Штатный GetFlags смотрит в NPC-мозг (HTN),
    /// которого у компаньона нет, и возвращал None — ЛЮБОЙ шлюз был для неё стеной, даже со своим
    /// доступом. Interact = «умеет открывать» + Access = «учитывай мой доступ» (КПК бармена).
    /// </summary>
    private NPCSteeringComponent RegisterSteering(EntityUid uid, Robust.Shared.Map.EntityCoordinates coords)
    {
        var steering = _steering.Register(uid, coords);
        steering.Flags = PathFlags.Interact | PathFlags.Access | PathFlags.Climbing;
        // КРИТИЧНО: Register по уже существующей компоненте НЕ сбрасывает статус. Если прошлый
        // маршрут закончился InRange/NoPath, стиринг считает «уже на месте» и не двигается —
        // из-за этого работало только первое перемещение после спавна.
        steering.Status = SteeringStatus.Moving;
        return steering;
    }

    // --- реализации инструментов (главный поток; текст-результат уходит модели сразу) ---

    /// <summary>
    /// Пока готовится напиток, другие поручения тела запрещены: они замещают текущее и обрывают
    /// конвейер (make_drink → go_to ломал готовку). Возвращает текст-отказ или null, если свободна.
    /// </summary>
    private string? BusyBrewing(LlmNpcComponent npc)
        => npc.PendingDrink is { } brewing
            ? $"Ты занята приготовлением «{brewing.Name}» — дождись готовности (вручится само, если указала person)."
            : null;

    /// <summary>go_to: пойти к названному человеку. Дорога доедет фоном через UpdateErrands.</summary>
    private string StartGoTo(EntityUid uid, string personName)
    {
        if (!TryComp<LlmNpcComponent>(uid, out var npc))
            return "Тело недоступно.";

        if (BusyBrewing(npc) is { } busy)
            return busy;

        if (FindPerson(uid, personName) is not { } target)
            return $"Не вижу рядом никого по имени «{personName}».";

        BeginErrand(uid, npc, LlmErrand.GoTo, target);
        return $"Ты пошла к {MetaData(target).EntityName}. Дойдёшь через пару секунд.";
    }

    /// <summary>give_item: отнести то, что в руке, человеку, вручить и вернуться на своё место.</summary>
    private string StartGive(EntityUid uid, string personName)
    {
        if (!TryComp<LlmNpcComponent>(uid, out var npc))
            return "Тело недоступно.";

        // Напиток ещё на конвейере — вручение случится само (или руки пока пусты не просто так).
        if (npc.PendingDrink is { } brewing)
        {
            return npc.PendingServe != null
                ? $"«{brewing.Name}» ещё готовится — ты вручишь его сама, give_item не нужен."
                : $"«{brewing.Name}» ещё готовится — дождись готовности, потом вручай.";
        }

        var item = _hands.EnumerateHeld((uid, null)).FirstOrDefault();
        if (item == default)
            return "В руках пусто — сначала возьми или приготовь то, что нужно отнести.";

        if (FindPerson(uid, personName) is not { } target)
            return $"Не вижу рядом никого по имени «{personName}».";

        BeginErrand(uid, npc, LlmErrand.GiveItem, target, item);
        return $"Ты понесла {MetaData(item).EntityName} к {MetaData(target).EntityName}: вручишь и вернёшься на место.";
    }

    /// <summary>pick_up: подойти к названному предмету неподалёку и поднять его.</summary>
    private string StartPickUp(EntityUid uid, string itemName)
    {
        if (!TryComp<LlmNpcComponent>(uid, out var npc))
            return "Тело недоступно.";

        if (BusyBrewing(npc) is { } busy)
            return busy;

        if (FindItem(uid, itemName) is not { } item)
            return $"Не вижу поблизости предмета «{itemName}».";

        BeginErrand(uid, npc, LlmErrand.PickUp, item);
        return $"Ты пошла за предметом «{MetaData(item).EntityName}» и поднимешь его.";
    }

    /// <summary>follow: ходить за человеком, пока не попросят прекратить.</summary>
    private string StartFollow(EntityUid uid, string personName)
    {
        if (!TryComp<LlmNpcComponent>(uid, out var npc))
            return "Тело недоступно.";

        if (BusyBrewing(npc) is { } busy)
            return busy;

        if (FindPerson(uid, personName) is not { } target)
            return $"Не вижу рядом никого по имени «{personName}».";

        BeginErrand(uid, npc, LlmErrand.Follow, target);
        return $"Ты идёшь следом за {MetaData(target).EntityName}, пока не попросят остановиться.";
    }

    /// <summary>
    /// walk: пройти в указанном направлении на N тайлов («иди туда», «отойди», «встань к окну»).
    /// Место (Home) переносится в точку назначения — она не убредает обратно.
    /// </summary>
    private string StartWalk(EntityUid uid, string direction, int tiles)
    {
        if (!TryComp<LlmNpcComponent>(uid, out var npc))
            return "Тело недоступно.";

        if (BusyBrewing(npc) is { } busy)
            return busy;

        tiles = Math.Clamp(tiles, 1, 15);
        var dir = direction.Trim().ToLowerInvariant() switch
        {
            "север" or "north" or "вверх" => new System.Numerics.Vector2(0, tiles),
            "юг" or "south" or "вниз" => new System.Numerics.Vector2(0, -tiles),
            "восток" or "east" or "вправо" or "направо" => new System.Numerics.Vector2(tiles, 0),
            "запад" or "west" or "влево" or "налево" => new System.Numerics.Vector2(-tiles, 0),
            _ => default,
        };
        if (dir == default)
            return "Не поняла направление: используй север/юг/запад/восток.";

        var target = Transform(uid).Coordinates.Offset(dir);
        npc.Errand = LlmErrand.Wander;
        npc.WanderTo = target;
        npc.ErrandTimeout = _timing.RealTime + TimeSpan.FromSeconds(20);
        npc.Home = target; // новое «своё место»
        EnsureComp<ActiveNPCComponent>(uid);
        RegisterSteering(uid, target).Range = ArriveRange - 0.2f;
        return $"Ты идёшь на {direction} ({tiles} шагов) и останешься там.";
    }

    /// <summary>stop: прекратить текущее занятие (следование, дорогу) и вернуться на своё место.</summary>
    private string StartStop(EntityUid uid)
    {
        if (!TryComp<LlmNpcComponent>(uid, out var npc))
            return "Тело недоступно.";

        if (npc.Errand == LlmErrand.None)
            return "Ты и так ничем не занята.";

        var wasBrewing = npc.PendingDrink?.Name;
        CancelErrand(uid, npc);
        // «Стой» значит стой: остаёмся здесь, и это место — новое «своё» (не убредать обратно).
        npc.Home = Transform(uid).Coordinates;
        return wasBrewing != null
            ? $"Ты бросила готовить «{wasBrewing}» и остановилась; теперь твоё место здесь."
            : "Ты остановилась; теперь твоё место здесь.";
    }

    // --- поиск целей рядом ---

    /// <summary>
    /// Ищет живого человека по имени рядом (тот же мир, радиус <see cref="FindRange"/>): точное имя,
    /// затем вхождение — модель может назвать «Иван», когда персонаж «Иван Петров».
    /// </summary>
    private EntityUid? FindPerson(EntityUid uid, string personName)
    {
        var name = personName.Trim().ToLowerInvariant();
        if (name.Length == 0)
            return null;

        var map = Transform(uid).MapID;
        var origin = _transform.GetWorldPosition(uid);

        EntityUid? partial = null;
        var query = EntityQueryEnumerator<MobStateComponent>();
        while (query.MoveNext(out var mob, out _))
        {
            if (mob == uid || Transform(mob).MapID != map)
                continue;
            if ((_transform.GetWorldPosition(mob) - origin).Length() > FindRange)
                continue;

            var mobName = MetaData(mob).EntityName.ToLowerInvariant();
            if (mobName == name)
                return mob;
            if (partial == null && (mobName.Contains(name) || name.Contains(mobName)))
                partial = mob;
        }

        return partial;
    }

    /// <summary>
    /// «Осматривается»: собирает, кого и что NPC видит вокруг — люди в радиусе поиска и свободные
    /// предметы поблизости. Уходит в промпт каждого раздумья, чтобы NPC знал обстановку сам, без слов.
    /// </summary>
    private string LookAround(EntityUid uid)
    {
        var map = Transform(uid).MapID;
        var origin = _transform.GetWorldPosition(uid);

        var people = new List<string>();
        var mobs = EntityQueryEnumerator<MobStateComponent>();
        while (mobs.MoveNext(out var mob, out var mobState))
        {
            if (mob == uid || Transform(mob).MapID != map)
                continue;
            var dist = (_transform.GetWorldPosition(mob) - origin).Length();
            if (dist > FindRange)
                continue;

            // Она видит и состояние: раненых, пьяных, лежащих — и может отреагировать по-человечески.
            var marks = new List<string>();
            if (dist <= 2.5f)
                marks.Add("вплотную");
            switch (mobState.CurrentState)
            {
                case Content.Shared.Mobs.MobState.Critical:
                    marks.Add("БЕЗ СОЗНАНИЯ, умирает!");
                    break;
                case Content.Shared.Mobs.MobState.Dead:
                    marks.Add("МЁРТВ");
                    break;
                default:
                    if (HasComp<Content.Shared.Damage.Components.DamageableComponent>(mob))
                    {
#pragma warning disable CS0618 // точное число не нужно — только градация «побит/цел»
                        var total = _damageable.GetTotalDamage(mob);
#pragma warning restore CS0618
                        if (total > 60)
                            marks.Add("серьёзно ранен");
                        else if (total > 25)
                            marks.Add("потрёпан");
                    }
                    if (_status.HasStatusEffect(mob, Content.Shared.Drunk.SharedDrunkSystem.Drunk))
                        marks.Add("навеселе");
                    break;
            }

            // Отношение к человеку на эту смену (set_attitude) — модель видит его каждый ход.
            if (TryComp<LlmNpcComponent>(uid, out var self)
                && self.Attitude.TryGetValue(MetaData(mob).EntityName, out var attitude))
                marks.Add($"твоё отношение: {attitude}");

            var entry = marks.Count > 0
                ? $"{MetaData(mob).EntityName} ({string.Join(", ", marks)})"
                : MetaData(mob).EntityName;

            // Внешность: флейвор-текст персонажа (то, что видно при осмотре) — коротко, для близких.
            // Она может отреагировать на вид человека, но промпт велит не зацикливаться.
            if (dist <= 5f && TryComp<Content.Shared.DetailExaminable.DetailExaminableComponent>(mob, out var detail))
            {
                var look = !string.IsNullOrWhiteSpace(detail.Content) ? detail.Content : detail.CharacterContent;
                if (!string.IsNullOrWhiteSpace(look))
                {
                    look = look.Replace('\n', ' ').Trim();
                    if (look.Length > 160)
                        look = look[..160] + "…";
                    entry += $" — внешность: «{look}»";
                }
            }

            people.Add(entry);
            if (people.Count >= 8)
                break;
        }

        var items = new List<string>();
        var query = EntityQueryEnumerator<ItemComponent>();
        while (query.MoveNext(out var item, out _))
        {
            var xform = Transform(item);
            if (xform.MapID != map || _container.IsEntityInContainer(item))
                continue;
            if ((_transform.GetWorldPosition(xform) - origin).Length() > 4f)
                continue;
            items.Add(MetaData(item).EntityName);
            if (items.Count >= 10)
                break;
        }

        var held = _hands.EnumerateHeld((uid, null))
            .Select(h => MetaData(h).EntityName)
            .ToList();

        var sb = new System.Text.StringBuilder();
        sb.Append("Что ты видишь прямо сейчас. Люди рядом: ");
        sb.Append(people.Count > 0 ? string.Join(", ", people) : "никого");
        sb.Append(". Предметы поблизости: ");
        sb.Append(items.Count > 0 ? string.Join(", ", items.Distinct()) : "ничего примечательного");
        sb.Append(". У тебя в руках: ");
        sb.Append(held.Count > 0 ? string.Join(", ", held) : "пусто");
        sb.Append('.');
        return sb.ToString();
    }

    /// <summary>Ищет предмет по названию рядом: ближайшее совпадение (точное имя приоритетнее).</summary>
    private EntityUid? FindItem(EntityUid uid, string itemName)
    {
        var name = itemName.Trim().ToLowerInvariant();
        if (name.Length == 0)
            return null;

        var map = Transform(uid).MapID;
        var origin = _transform.GetWorldPosition(uid);

        EntityUid? best = null;
        var bestDist = float.MaxValue;
        var bestExact = false;

        var query = EntityQueryEnumerator<ItemComponent>();
        while (query.MoveNext(out var item, out _))
        {
            var xform = Transform(item);
            if (xform.MapID != map)
                continue;
            // Внутри контейнера (в чьих-то руках, в ящике) — не подобрать с пола.
            if (_container.IsEntityInContainer(item))
                continue;

            var dist = (_transform.GetWorldPosition(xform) - origin).Length();
            if (dist > FindRange)
                continue;

            var entName = MetaData(item).EntityName.ToLowerInvariant();
            var exact = entName == name;
            if (!exact && !entName.Contains(name))
                continue;

            // Точное имя бьёт частичное; при равенстве — берём ближайший.
            if (exact && !bestExact || exact == bestExact && dist < bestDist)
            {
                best = item;
                bestDist = dist;
                bestExact = exact;
            }
        }

        return best;
    }
}
