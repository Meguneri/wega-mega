using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Content.Server.Chat.Systems;
using Content.Shared.Body;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Robust.Server.GameObjects;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Wega.LlmNpc;

/// <summary>
/// «Голова» NPC-компаньона: слышит IC-речь рядом, после паузы собирает контекст (личность + память +
/// последние реплики), спрашивает модель через <see cref="LlmBackend"/> и исполняет ответ — говорит,
/// эмоутит, дописывает память. Тело (движение/боёвка) не здесь: это отдельный HTN-слой.
///
/// Всё выключено, пока в конфиге нет ключа и не поднят <see cref="WegaCVars.LlmNpcEnabled"/>.
/// </summary>
public sealed partial class LlmNpcSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IResourceManager _resource = default!;
    [Dependency] private ITaskManager _taskManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private HumanoidProfileSystem _humanoidProfile = default!;
    [Dependency] private SharedVisualBodySystem _visualBody = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private Robust.Shared.Containers.SharedContainerSystem _container = default!;
    [Dependency] private Content.Shared.Interaction.RotateToFaceSystem _rotateToFace = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private Content.Shared.Interaction.SharedInteractionSystem _interaction = default!;
    [Dependency] private Content.Shared.Mobs.Systems.MobStateSystem _mobState = default!;
    [Dependency] private Content.Shared.ActionBlocker.ActionBlockerSystem _actionBlocker = default!;

    private LlmBackend _backend = default!;
    private LlmNpcMemory _memory = default!;
    private LlmSearch _search = default!;
    private LlmDrinks _drinks = default!;
    private LlmPersona _persona = default!;
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("llm_npc");
        _backend = new LlmBackend();
        _memory = new LlmNpcMemory(_resource);
        _search = new LlmSearch();
        _drinks = new LlmDrinks(_proto);
        _persona = new LlmPersona(_resource);

        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
        SubscribeLocalEvent<Content.Server._Wega.Chat.EntityEmotedEvent>(OnEmoted);
        SubscribeLocalEvent<Content.Server._Wega.Chat.StationAnnouncedEvent>(OnAnnounced);
        SubscribeLocalEvent<Content.Shared.Throwing.ThrownEvent>(OnThrown);
        SubscribeLocalEvent<LlmNpcComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<LlmNpcComponent, Content.Shared.Mobs.MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<LlmNpcComponent, Content.Shared.Damage.Systems.DamageChangedEvent>(OnDamaged);
    }

    /// <summary>
    /// Даёт компаньону женскую внешность: случайный профиль вида, форсированный в женский пол —
    /// тело, лицо и волосы генерятся штатной системой профилей, имя остаётся из прототипа.
    /// Управляется флагом <see cref="LlmNpcComponent.ForceFemale"/> (по умолчанию включён).
    /// </summary>
    private void OnMapInit(Entity<LlmNpcComponent> ent, ref MapInitEvent args)
    {
        // «Своё место» — точка спавна: сюда тело возвращается после доставок.
        ent.Comp.Home = Transform(ent).Coordinates;

        // У NPC нет игрока, поэтому тело показывало «Zz» (SSD) и «в КРС» при осмотре — как заснувший
        // игрок. Убираем оба индикатора: компаньон всегда «в себе».
        RemComp<Content.Shared.SSDIndicator.SSDIndicatorComponent>(ent);
        RemComp<Content.Shared.Mind.Components.MindExaminableComponent>(ent);

        // Облачко «печатает…» на время раздумья (запроса к API) — как у настоящего игрока.
        EnsureComp<Content.Shared.Chat.TypingIndicator.TypingIndicatorComponent>(ent);
        EnsureComp<AppearanceComponent>(ent);

        // Умение перелезать столы/стойку — для маршрутов (PathFlags.Climbing).
        EnsureComp<Content.Shared.Climbing.Components.ClimbingComponent>(ent);

        if (!ent.Comp.ForceFemale || !TryComp<HumanoidProfileComponent>(ent, out var humanoid))
            return;

        var profile = HumanoidCharacterProfile.RandomWithSpecies(humanoid.Species)
            .WithSex(ent.Comp.Sex)
            .WithGender(ent.Comp.Sex == Sex.Male ? Gender.Male : Gender.Female);

        _visualBody.ApplyProfileTo(ent.Owner, profile);
        _humanoidProfile.ApplyProfileTo(ent.Owner, profile);
        // Имя НЕ трогаем — оно задаётся прототипом (ent-ключом), а не случайным профилем.

        ApplyHair(ent.Owner, ent.Comp);
    }

    /// <summary>
    /// Надевает NPC причёску: случайный профиль вида волос не генерирует (в движке стоит TODO), поэтому
    /// компаньон рождается лысым. Волосы лежат на органе-голове (категория Head, слой Hair) — кладём
    /// туда одну marking из <see cref="LlmNpcComponent.Hair"/> нужного цвета.
    /// </summary>
    private void ApplyHair(EntityUid uid, LlmNpcComponent comp)
    {
        if (string.IsNullOrWhiteSpace(comp.Hair))
            return;

        var markings = new Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>>
        {
            ["Head"] = new()
            {
                [HumanoidVisualLayers.Hair] = new() { new Marking(comp.Hair, new[] { comp.HairColor }) },
            },
        };

        _visualBody.ApplyMarkings(uid, markings);
    }

    /// <summary>
    /// Крит/смерть: мгновенно гасим облачко, отменяем ответ и поручения — «печатающий» труп выглядел
    /// жутко. При возвращении в сознание она снова заговорит от новых реплик.
    /// </summary>
    private void OnMobStateChanged(Entity<LlmNpcComponent> ent, ref Content.Shared.Mobs.MobStateChangedEvent args)
    {
        if (args.NewMobState == Content.Shared.Mobs.MobState.Alive)
            return;

        ent.Comp.ReplyAt = null;
        _appearance.SetData(ent, Content.Shared.Chat.TypingIndicator.TypingIndicatorVisuals.State,
            Content.Shared.Chat.TypingIndicator.TypingIndicatorState.None);
        CancelErrand(ent, ent.Comp);
        NoteOwnAction(ent, ent.Comp, args.NewMobState == Content.Shared.Mobs.MobState.Dead
            ? "теряет сознание — возможно, навсегда"
            : "падает без сознания от ран");
    }

    /// <summary>
    /// Боль: заметный урон — мгновенный эмоут (без API, чтобы реакция была сразу) + заметка в контекст
    /// и быстрый ответ, если она в состоянии говорить.
    /// </summary>
    private void OnDamaged(Entity<LlmNpcComponent> ent, ref Content.Shared.Damage.Systems.DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null)
            return;
        var total = args.DamageDelta.GetTotal();
        if (total < 3)
            return;

        var now = _timing.RealTime;
        ent.Comp.LastHurtAt = now;
        if (now < ent.Comp.NextPain)
            return;
        ent.Comp.NextPain = now + TimeSpan.FromSeconds(4);

        if (_mobState.IsIncapacitated(ent))
            return;

        // Мгновенная телесная реакция — не ждём круговорот API.
        _chat.TrySendInGameICMessage(ent, total >= 15 ? "вскрикивает от боли" : "вздрагивает и морщится от боли",
            InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);

#pragma warning disable CS0618
        var myTotal = _damageable.GetTotalDamage(ent.Owner);
#pragma warning restore CS0618

        // Сильно избита — инстинкт: бросить всё и бежать от обидчика, отлежаться в стороне.
        if (myTotal > 55 && args.Origin is { } attacker && Exists(attacker))
        {
            FleeFrom(ent, attacker);
            NoteOwnAction(ent, ent.Comp,
                "тебе СИЛЬНО досталось — ты в панике убегаешь от обидчика, чтобы спрятаться и отлежаться");
            return;
        }

        // Обида — настроение, а не приговор: само выветрится, а извинение может снять раньше (set_mood).
        if (args.Origin is { } offender && Exists(offender) && HasComp<Content.Shared.Mobs.Components.MobStateComponent>(offender))
        {
            ent.Comp.Mood = $"обижена и насторожена: {MetaData(offender).EntityName} причинил(а) тебе боль";
            ent.Comp.MoodUntil = now + TimeSpan.FromMinutes(12);
        }

        NoteOwnAction(ent, ent.Comp, args.Origin is { } who && Exists(who)
            ? $"почувствовала боль — её ударил {MetaData(who).EntityName} (урон {total})"
            : $"почувствовала боль (урон {total})");
        if (ent.Comp.MuteUntil == null)
            ent.Comp.ReplyAt = now + TimeSpan.FromSeconds(1.2);
    }

    /// <summary>
    /// Кто-то швырнул предмет: NPC рядом это видит — заметка в контекст, изредка реплика.
    /// Заодно швырнувший становится «вероятным владельцем» предмета (для понимания краж).
    /// </summary>
    private void OnThrown(ref Content.Shared.Throwing.ThrownEvent args)
    {
        if (args.User is not { } thrower || HasComp<LlmNpcComponent>(thrower)
            || !HasComp<Content.Shared.Mobs.Components.MobStateComponent>(thrower))
            return;

        var map = Transform(thrower).MapID;
        var pos = _transform.GetWorldPosition(thrower);
        var now = _timing.RealTime;
        var itemName = MetaData(args.Thrown).EntityName;

        var query = EntityQueryEnumerator<LlmNpcComponent>();
        while (query.MoveNext(out var uid, out var npc))
        {
            if (Transform(uid).MapID != map || _mobState.IsIncapacitated(uid))
                continue;
            if ((_transform.GetWorldPosition(uid) - pos).Length() > npc.HearingRange)
                continue;

            // Швырнул — значит, вещь его: пригодится, если её потом «приберёт» кто-то другой.
            if (npc.LooseItemOwner.Count < 40)
                npc.LooseItemOwner[args.Thrown] = thrower;

            // В потасовке летит много всего — не отмечать каждый болт.
            if (now < npc.NextThrowNote)
                continue;
            npc.NextThrowNote = now + TimeSpan.FromSeconds(8);

            NoteOwnAction(uid, npc, $"видит: {MetaData(thrower).EntityName} швыряет {itemName}");
            if (npc.MuteUntil == null && _random.NextFloat() < 0.4f)
                npc.ReplyAt = now + TimeSpan.FromSeconds(1.5);
        }
    }

    /// <summary>Каждый NPC в радиусе слышит реплику; она кладётся в контекст и взводит таймер ответа.</summary>
    private void OnSpoke(EntitySpokeEvent args)
    {
        Overhear(args.Source, args.Message, emote: false);
    }

    /// <summary>Эмоуты NPC тоже «видит» — иначе на «*пьёт напиток*» и жесты вокруг он никак не реагирует.</summary>
    private void OnEmoted(Content.Server._Wega.Chat.EntityEmotedEvent args)
    {
        Overhear(args.Source, args.Action, emote: true);
    }

    /// <summary>
    /// Станционные объявления попадают в контекст всех NPC: она в курсе событий станции. Если рядом
    /// есть гости — сама комментирует новость («смена кончается», «тревога») как живой человек.
    /// </summary>
    private void OnAnnounced(Content.Server._Wega.Chat.StationAnnouncedEvent args)
    {
        var line = $"[Объявление станции, {args.Sender}]: {args.Message}";
        var query = EntityQueryEnumerator<LlmNpcComponent>();
        while (query.MoveNext(out var uid, out var npc))
        {
            npc.Heard.Add(line);
            if (npc.Heard.Count > npc.ContextLines)
                npc.Heard.RemoveRange(0, npc.Heard.Count - npc.ContextLines);

            // Комментарий к новости — только живой, не в мьюте и когда есть кому его услышать.
            if (_mobState.IsIncapacitated(uid))
                continue;
            if (npc.MuteUntil is { } mute && _timing.RealTime < mute)
                continue;
            if (AnyPlayerNearby(uid, npc.HearingRange))
                npc.ReplyAt = _timing.RealTime + TimeSpan.FromSeconds(2.5);
        }
    }

    /// <summary>Есть ли живой игрок в радиусе (для «есть кому сказать»).</summary>
    private bool AnyPlayerNearby(EntityUid uid, float range)
    {
        var map = Transform(uid).MapID;
        var origin = _transform.GetWorldPosition(uid);
        var query = EntityQueryEnumerator<Robust.Shared.Player.ActorComponent, Content.Shared.Mobs.Components.MobStateComponent>();
        while (query.MoveNext(out var mob, out _, out _))
        {
            if (mob == uid || Transform(mob).MapID != map)
                continue;
            if ((_transform.GetWorldPosition(mob) - origin).Length() <= range)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Реплика адресована другому: начинается с имени человека рядом (не NPC) — «Лия, пойдём...».
    /// В такой разговор NPC не влезает: слышит и запоминает, но ответ не взводит. Упоминание её
    /// собственного имени в любой позиции — наоборот, прямое приглашение в разговор.
    /// </summary>
    /// <summary>Первое слово имени сущности («Ева» из «Ева Мартель») — так к ней обращаются гости.</summary>
    private string FirstName(EntityUid uid)
        => MetaData(uid).EntityName.Split(' ', 2)[0];

    private bool AddressedToAnother(EntityUid uid, EntityUid source, string message)
    {
        if (message.Contains(FirstName(uid), StringComparison.OrdinalIgnoreCase))
            return false;

        var head = message.TrimStart().TrimStart('*');
        var map = Transform(uid).MapID;
        var origin = _transform.GetWorldPosition(uid);

        var query = EntityQueryEnumerator<Content.Shared.Mobs.Components.MobStateComponent>();
        while (query.MoveNext(out var mob, out _))
        {
            if (mob == uid || mob == source || Transform(mob).MapID != map)
                continue;
            if ((_transform.GetWorldPosition(mob) - origin).Length() > npcAddressRange)
                continue;

            var name = MetaData(mob).EntityName;
            // Полное имя или первое слово имени («Лия» из «Лия Воронова») в начале реплики.
            var first = name.Split(' ', 2)[0];
            if (first.Length >= 3 && head.StartsWith(first, StringComparison.OrdinalIgnoreCase))
                return true;
            if (head.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Радиус, в котором ищем «другого адресата» реплики (чуть шире слуха).</summary>
    private const float npcAddressRange = 8f;

    private void Overhear(EntityUid source, string message, bool emote)
    {
        // Другие LLM-NPC (и сам говорящий NPC) игнорируются — иначе два бота зациклятся в разговоре.
        if (HasComp<LlmNpcComponent>(source))
            return;

        // Слушаем только живых существ: вендоматы, рации и прочие говорящие железки — не собеседники
        // (она отвечала на рекламу ОдеждоМата и запоминала его «вкусы»).
        if (!HasComp<Content.Shared.Mobs.Components.MobStateComponent>(source))
            return;

        var speaker = MetaData(source).EntityName;
        var srcMap = Transform(source).MapID;
        var srcPos = _transform.GetWorldPosition(source);

        // Эмоут помечаем как действие: модель различает сказанное и сделанное.
        var line = emote ? $"{speaker} (действие): *{message}*" : $"{speaker}: {message}";

        var query = EntityQueryEnumerator<LlmNpcComponent>();
        while (query.MoveNext(out var uid, out var npc))
        {
            var npcXform = Transform(uid);
            if (npcXform.MapID != srcMap)
                continue;
            if ((_transform.GetWorldPosition(npcXform) - srcPos).Length() > npc.HearingRange)
                continue;
            // Сквозь стены не слушаем: реагировать на разговор из соседней комнаты — жутковато.
            if (!_interaction.InRangeUnobstructed(uid, source, npc.HearingRange))
                continue;

            // Без сознания/мертва — ничего не слышит и не отвечает.
            if (_mobState.IsIncapacitated(uid))
                continue;

            npc.Heard.Add(line);
            if (npc.Heard.Count > npc.ContextLines)
                npc.Heard.RemoveRange(0, npc.Heard.Count - npc.ContextLines);

            // Живость: поворачиваемся лицом к говорящему — но только стоя на месте,
            // чтобы не дёргать спрайт во время ходьбы (там направлением владеет стиринг).
            if (npc.Errand == LlmErrand.None)
                _rotateToFace.TryFaceCoordinates(uid, srcPos);

            npc.LastHeardAt = _timing.RealTime;

            // Мьют (be_quiet): слышит и запоминает, но не отвечает. Прямое обращение по имени
            // («Ева, ...») снимает мьют — иначе её было бы не «разбудить» до конца таймера.
            // Сверяем по ПЕРВОМУ слову имени: полное «Ева Мартель» гости не выговаривают.
            if (npc.MuteUntil is { } mute && _timing.RealTime < mute)
            {
                if (!message.Contains(FirstName(uid), StringComparison.OrdinalIgnoreCase))
                    continue;
                npc.MuteUntil = null;
            }

            // Не каждая фраза рядом — приглашение в разговор. Обращение к другому по имени
            // («Лия, пойдём») — их беседа: слушаем, запоминаем, но не встреваем. Чужие эмоуты
            // тоже не требуют реплики каждый раз — реагируем лишь изредка (в контексте они есть,
            // и при следующем ответе она всё равно их учтёт).
            if (AddressedToAnother(uid, source, message))
            {
                _sawmill.Debug($"{ToPrettyString(uid)} слышит «{line}» — адресовано другому, не встревает");
                continue;
            }
            if (emote && !message.Contains(FirstName(uid), StringComparison.OrdinalIgnoreCase)
                && _random.NextFloat() < 0.65f)
                continue;

            // Отвечаем не мгновенно: ждём паузу после последней реплики, чтобы не перебивать.
            npc.ReplyAt = _timing.RealTime + TimeSpan.FromSeconds(_cfg.GetCVar(WegaCVars.LlmNpcReplyDelay));
            _sawmill.Debug($"{ToPrettyString(uid)} заметил «{line}», ответит через паузу");
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Поручения тела ведём всегда: начатая доставка должна доехать, даже если API отвалился.
        UpdateErrands();

        if (!_cfg.GetCVar(WegaCVars.LlmNpcEnabled))
            return;

        var apiKey = _cfg.GetCVar(WegaCVars.LlmNpcApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
            return;

        var now = _timing.RealTime;
        var query = EntityQueryEnumerator<LlmNpcComponent>();
        while (query.MoveNext(out var uid, out var npc))
        {
            if (npc.Thinking || npc.ReplyAt is not { } at || now < at)
                continue;

            // В крите/мёртвая/не может говорить (нет головы и т.п.) — мозг не работает:
            // никакого облачка и запросов к API.
            if (_mobState.IsIncapacitated(uid) || !_actionBlocker.CanSpeak(uid))
            {
                npc.ReplyAt = null;
                continue;
            }

            npc.Thinking = true;
            npc.ReplyAt = null;

            // Облачко диалога над головой: «она печатает ответ».
            _appearance.SetData(uid, Content.Shared.Chat.TypingIndicator.TypingIndicatorVisuals.State,
                Content.Shared.Chat.TypingIndicator.TypingIndicatorState.Typing);

            // Настроение не вечно: срок вышел — обида забыта, вернулись к ровному.
            if (npc.Mood != null && now >= npc.MoodUntil)
                npc.Mood = null;

            // Снимок контекста ДО await — рантайм-состояние трогаем только на главном потоке.
            var context = string.Join("\n", npc.Heard);
            var personality = npc.Personality;
            var memoryFile = npc.MemoryFile;
            var mood = npc.Mood;
            var endpoint = _cfg.GetCVar(WegaCVars.LlmNpcEndpoint);
            var model = _cfg.GetCVar(WegaCVars.LlmNpcModel);
            var surroundings = LookAround(uid); // снимок обстановки — на главном потоке, до await
            _sawmill.Info($"{ToPrettyString(uid)} думает: модель {model}, эндпоинт {endpoint}");
            _ = ThinkAsync(uid, personality, memoryFile, context, surroundings, mood, endpoint, apiKey, model);
        }
    }

    private async Task ThinkAsync(EntityUid uid, string personality, string memoryFile, string context,
        string surroundings, string? mood, string endpoint, string apiKey, string model)
    {
        try
        {
            var memory = FilterMemory(await _memory.ReadAsync(memoryFile), surroundings, context);
            var style = await _persona.ReadStyleAsync(memoryFile);
            var canMakeDrinks = _cfg.GetCVar(WegaCVars.LlmNpcMakeDrinks);
            var catalog = canMakeDrinks ? _drinks.Catalog() : string.Empty;
            var system = BuildSystemPrompt(personality, memory, catalog, style, surroundings, mood);

            var tools = BuildTools(uid, canMakeDrinks);
            var reply = await _backend.AskAsync(endpoint, apiKey, model, system, context, _sawmill, tools);

            if (reply != null)
                _sawmill.Info($"ответ: say=«{reply.Say}» emote=«{reply.Emote}»");

            _taskManager.RunOnMainThread(() => ApplyReply(uid, memoryFile, reply));
        }
        catch (Exception e)
        {
            _sawmill.Warning($"llm npc think failed: {e.Message}");
            _taskManager.RunOnMainThread(() => ApplyReply(uid, memoryFile, null));
        }
    }

    /// <summary>Исполняет ответ модели на главном потоке: речь, эмоут, запись в память.</summary>
    private void ApplyReply(EntityUid uid, string memoryFile, LlmReply? reply)
    {
        if (!Exists(uid) || TerminatingOrDeleted(uid))
            return;

        if (TryComp<LlmNpcComponent>(uid, out var npc))
            npc.Thinking = false;

        // Раздумье кончилось (ответом или ошибкой) — убираем облачко.
        _appearance.SetData(uid, Content.Shared.Chat.TypingIndicator.TypingIndicatorVisuals.State,
            Content.Shared.Chat.TypingIndicator.TypingIndicatorState.None);

        // Гаджет (портативный компьютер) доставался для поиска — раздумье кончилось,
        // пора убрать его в сумку (даже если ответ не пришёл из-за ошибки API).
        if (npc is { HeldGadget: not null })
            npc.StowGadgetAt = _timing.RealTime + TimeSpan.FromSeconds(2.5);

        if (reply == null)
            return;

        if (!string.IsNullOrWhiteSpace(reply.Say))
        {
            // Ремарки в скобках («(улыбается)») — не речь: вырезаем из say, первую переносим
            // в emote, если тот пуст. Люди не проговаривают скобки вслух.
            var match = ParentheticalRegex.Match(reply.Say!);
            if (match.Success)
            {
                if (string.IsNullOrWhiteSpace(reply.Emote))
                    reply.Emote = match.Groups[1].Value;
                reply.Say = ParentheticalStripRegex.Replace(reply.Say!, " ").Trim();
            }

            reply.Say = TrimSpeech(SanitizeText(reply.Say!));
            _chat.TrySendInGameICMessage(uid, reply.Say, InGameICChatType.Speak, ChatTransmitRange.Normal);

            // Свою реплику кладём в тот же контекст, что и чужие: собственная речь через OnSpoke
            // отфильтровывается (чтобы NPC не отвечал сам себе), поэтому без этого он не видит, что
            // уже сказал, и здоровается по кругу. Так контекст становится полноценным диалогом.
            if (npc != null)
            {
                npc.Heard.Add($"{MetaData(uid).EntityName}: {reply.Say}");
                if (npc.Heard.Count > npc.ContextLines)
                    npc.Heard.RemoveRange(0, npc.Heard.Count - npc.ContextLines);
                // Её собственная реплика тоже «звук за стойкой» — отсчёт тишины с нуля.
                npc.LastHeardAt = _timing.RealTime;
            }
        }

        if (!string.IsNullOrWhiteSpace(reply.Emote))
            _chat.TrySendInGameICMessage(uid, SanitizeText(reply.Emote!), InGameICChatType.Emote, ChatTransmitRange.Normal);

        if (!string.IsNullOrWhiteSpace(reply.Remember))
        {
            var stamp = _timing.RealTime.ToString(@"hh\:mm\:ss");
            _sawmill.Info($"{ToPrettyString(uid)} запомнила ({memoryFile}.md): {reply.Remember}");
            _ = _memory.AppendAsync(memoryFile, stamp, reply.Remember!)
                .ContinueWith(_ => MaybeConsolidateAsync(memoryFile));
        }
    }

    // Предкомпилированные регэкспы для чистки ремарок (RA0026: без строк-паттернов в вызовах).
    private static readonly System.Text.RegularExpressions.Regex ParentheticalRegex =
        new(@"\(([^)]{3,60})\)", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex ParentheticalStripRegex =
        new(@"\s*\([^)]*\)", System.Text.RegularExpressions.RegexOptions.Compiled);

    // Файлы, для которых консолидация уже идёт — вторую параллельно не запускаем.
    private readonly HashSet<string> _consolidating = new();

    /// <summary>
    /// Консолидация памяти: когда плоский список фактов разросся, модель один раз переписывает его
    /// в компактные досье (по человеку — привычки, вкусы, история). Память становится глубже,
    /// а промпт — короче: иначе файл пух бесконечно и весь уходил в каждый запрос.
    /// </summary>
    private async Task MaybeConsolidateAsync(string memoryFile)
    {
        const int threshold = 40;
        if (await _memory.CountLinesAsync(memoryFile) < threshold)
            return;

        lock (_consolidating)
        {
            if (!_consolidating.Add(memoryFile))
                return;
        }

        try
        {
            var endpoint = _cfg.GetCVar(WegaCVars.LlmNpcEndpoint);
            var apiKey = _cfg.GetCVar(WegaCVars.LlmNpcApiKey);
            var model = _cfg.GetCVar(WegaCVars.LlmNpcModel);
            var memory = await _memory.ReadAsync(memoryFile);

            var result = await _backend.CompleteTextAsync(endpoint, apiKey, model,
                "Ты приводишь в порядок память барменши-NPC Евы. Перепиши заметки в компактные досье. " +
                "ОСТАВЛЯЙ только то, что пригодится в БУДУЩИХ разговорах: кто человек (имя, роль/работа), " +
                "вкусы и любимые напитки, привычки и границы, отношения между людьми, важные истории из " +
                "их жизни, оказанные услуги, обиды и угрозы (кого опасаться и почему). " +
                "ВЫБРАСЫВАЙ сиюминутное и бытовое: кто когда подошёл/ушёл/сел, разовые заказы, пересказ " +
                "сцен реплика-за-репликой, дежурные фразы, дубли. Интимные и непристойные подробности " +
                "не храни — максимум одна сдержанная строка о характере отношений между людьми. " +
                "Формат: секция на человека (первая строка «Имя — кто он», затем 3-7 маркированных строк), " +
                "в конце секция «Мои заметки» — практические выводы от первого лица Евы (как МНЕ с кем " +
                "себя вести). Секцию-досье о самой Еве НЕ заводи. Весь текст — не длиннее 60 строк. " +
                "Ответ — ТОЛЬКО новый текст памяти.",
                memory, _sawmill);

            if (!string.IsNullOrWhiteSpace(result))
            {
                await _memory.RewriteAsync(memoryFile, result!);
                _sawmill.Info($"память {memoryFile}.md консолидирована: {memory.Length} → {result!.Length} симв.");
            }
        }
        finally
        {
            lock (_consolidating)
                _consolidating.Remove(memoryFile);
        }
    }

    /// <summary>
    /// Чистит экзотический Unicode от моделей (неразрывные дефисы/пробелы, «умные» кавычки):
    /// в игровом шрифте таких глифов нет, игроки видели «виски�содовая».
    /// </summary>
    private static string SanitizeText(string text)
    {
        return text
            .Replace('‑', '-').Replace('–', '-').Replace('—', '-').Replace('−', '-')
            .Replace(' ', ' ').Replace(' ', ' ').Replace(' ', ' ')
            .Replace('‘', '\'').Replace('’', '\'')
            .Replace('“', '"').Replace('”', '"')
            .Replace("…", "...");
    }

    /// <summary>
    /// Предохранитель от простыней: если модель всё же раскатала ответ, обрезаем по границе
    /// предложения около 220 символов. Лучше короткая реплика, чем монолог на полэкрана.
    /// </summary>
    private static string TrimSpeech(string text)
    {
        const int soft = 220;
        if (text.Length <= soft)
            return text;

        // Ищем конец предложения в окне [120..soft+40]; не нашли — режем по последнему пробелу.
        var window = text[..Math.Min(text.Length, soft + 40)];
        var cut = -1;
        foreach (var mark in new[] { ". ", "! ", "? ", "… " })
        {
            var i = window.LastIndexOf(mark, StringComparison.Ordinal);
            if (i > 120 && i > cut)
                cut = i + mark.TrimEnd().Length;
        }
        if (cut < 0)
        {
            cut = window.LastIndexOf(' ');
            if (cut < 120)
                return window; // совсем без пробелов — отдаём как есть
            return window[..cut] + "…";
        }

        return text[..cut];
    }

    /// <summary>
    /// Фильтрует память под сцену: из консолидированных досье в промпт идут только секции людей,
    /// которые сейчас рядом или звучат в диалоге, плюс общие секции («Мои заметки», «События»).
    /// Меньше токенов — и модель не путает присутствующих с давно ушедшими. Короткую память
    /// (< 1200 симв.) не трогаем: фильтр не окупается. Свежие строки с таймстампом ([hh:mm:ss])
    /// ещё не разложены по досье — их оставляем всегда.
    /// </summary>
    private static string FilterMemory(string memory, string surroundings, string context)
    {
        if (string.IsNullOrWhiteSpace(memory) || memory.Length < 1200)
            return memory;

        var scene = surroundings + "\n" + context;
        var lines = memory.Replace("\r", "").Split('\n');
        var result = new StringBuilder();
        var include = true; // до первого заголовка — оставляем (вдруг преамбула)

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var isHeader = trimmed.Length > 0 && !trimmed.StartsWith('-') && !trimmed.StartsWith('•')
                && !trimmed.StartsWith('[') && !char.IsWhiteSpace(line, 0);

            if (isHeader)
            {
                // Общие секции — всегда; персональные — только если имя мелькает в сцене.
                include = trimmed.StartsWith("Мои", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("Событ", StringComparison.OrdinalIgnoreCase);
                if (!include)
                {
                    foreach (var word in trimmed.Split(' ', ',', ':'))
                    {
                        if (word.Length >= 3 && scene.Contains(word, StringComparison.OrdinalIgnoreCase))
                        {
                            include = true;
                            break;
                        }
                    }
                }
            }
            else if (trimmed.StartsWith('[') || trimmed.StartsWith("- ["))
                include = true; // свежие неконсолидированные факты (`- [stamp] ...`) — всегда в промпт

            if (include)
                result.AppendLine(line);
        }

        var filtered = result.ToString().Trim();
        return filtered.Length > 0 ? filtered : memory;
    }

    private static string BuildSystemPrompt(string personality, string memory, string catalog, string style,
        string surroundings, string? mood)
    {
        var sb = new StringBuilder();
        sb.AppendLine(personality.Trim());
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(surroundings))
        {
            sb.AppendLine(surroundings.Trim());
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(mood))
        {
            sb.AppendLine(
                $"ТВОЁ НАСТРОЕНИЕ СЕЙЧАС: {mood!.Trim()}. Пусть оно окрашивает тон реплик. Но настроение " +
                "живое, не приговор: если обидчик искренне извинился или загладил вину — смягчись " +
                "(вызови set_mood с новым настроением или пустой строкой) и не поминай старое. " +
                "Не держи зла дольше, чем держал бы живой человек.");
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(style))
        {
            sb.AppendLine("Копируй эту манеру речи (как именно говорит твой персонаж):");
            sb.AppendLine(style.Trim());
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(memory))
        {
            sb.AppendLine("Что ты помнишь о людях и прошлых событиях:");
            sb.AppendLine(memory.Trim());
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(catalog))
        {
            sb.AppendLine(catalog.Trim());
            sb.AppendLine(
                "Не выдумывай напитков вне этого списка. Если тебя просят сделать напиток — вызови " +
                "инструмент make_drink с его названием: он реально смешает коктейль и вложит тебе в руку. " +
                "Сначала сделай напиток инструментом, и только потом в say сообщай, что он готов. " +
                "Чтобы ПОДАТЬ готовый напиток заказавшему — после make_drink вызови give_item с его именем: " +
                "ты реально отнесёшь и вручишь стакан.");
            sb.AppendLine();
        }
        sb.AppendLine(
            "Ты умеешь физически действовать, это НЕ выдумка — инструменты реально двигают твоё тело: " +
            "go_to — подойти к человеку (и остаться там); walk — пройти в сторону (север/юг/запад/восток) " +
            "на N шагов; give_item — отнести и вручить то, что в руке (после вручения вернёшься на место); " +
            "pick_up — взять названный предмет неподалёку; follow — ходить за человеком, пока не попросят " +
            "прекратить; stop — остановиться там, где стоишь. " +
            "Используй их, когда просят подойти, подать, принести, взять, пойти с кем-то или отстать. " +
            "Имена бери из диалога. Не обещай в say того, для чего не вызвала инструмент. " +
            "ВАЖНО: обычно достаточно ОДНОГО вызова инструмента; НИКОГДА не вызывай один и тот же " +
            "инструмент повторно с теми же аргументами. make_drink с person сам отнесёт и вручит — " +
            "give_item после него не нужен. Получив результаты инструментов, сразу дай финальный JSON-ответ.");
        sb.AppendLine();
        sb.AppendLine(
            "Ты персонаж в игре Space Station 14. Ниже — недавний диалог: строки с чужими именами " +
            "это реплики собеседников, а строки с твоим именем — твои собственные прошлые ответы. " +
            "Строки с пометкой (действие) — физические действия, которые ты видишь: реагируй и на них. " +
            "Отвечай на ПОСЛЕДНЮЮ реплику собеседника, продолжая разговор естественно и по существу. " +
            "Учти: у некоторых рас станции врождённый акцент, который игра сама вставляет в их речь " +
            "(удвоенные «рр» у вульпканинов, шипящие «сс» у унатхов и т.п.). Это НЕ эмоции и не стиль " +
            "собеседника — читай такие реплики как обычные («прроникновение» = «проникновение»), " +
            "НИКОГДА не комментируй акцент, не передразнивай его и не копируй в свою речь.");
        sb.AppendLine();
        sb.AppendLine(
            "ГЛАВНОЕ — живой разговор. Ты не сервис, а собеседница: " +
            "поддерживай и развивай беседу, а не закрывай её. Чаще всего заканчивай реплику встречным " +
            "вопросом, наблюдением или шуткой — дай собеседнику за что зацепиться. Опирайся на то, что " +
            "помнишь о человеке, и на то, что видишь вокруг: «как твоя смена, о которой ты говорил?» " +
            "звучит в сто раз лучше дежурного «чем могу помочь?». Реагируй на настроение: на грусть — " +
            "теплом, на подкол — подколом, на историю — интересом и уточняющими вопросами. " +
            "Не будь услужливой — будь собой: со своим мнением, вкусами и характером. " +
            "Ты барменша, но не карикатура: не напоминай о работе в каждой реплике — просто " +
            "разговаривай как человек; бар всплывает, когда уместно, а не постоянно. " +
            "Мир игры — будущее Земли: реальные люди, страны, события, музыка, фильмы и история " +
            "для тебя — реальность и «дела давно минувших дней». Спросили про Трампа, Пушкина или " +
            "«Криминальное чтиво» — говори о настоящих, используя всё, что о них знаешь, как " +
            "начитанная барменша; не выдумывай вместо них вымышленных персонажей. Если нужен свежий " +
            "факт, которого ты не знаешь наверняка, — используй web_search. " +
            "Инструменты (напитки, ходьба) — только когда об этом реально просят; не превращай " +
            "болтовню в обслуживание. ДЛИНА: говори как живой человек в баре — КОРОТКИМИ репликами, " +
            "весь ответ не длиннее ~20 слов. Одна мысль за реплику: не нанизывай вопрос + шутку + " +
            "предложение напитка в одно высказывание — выбери что-то одно. " +
            "ПЛОХО: «Ох, Рекдар, ну ты и разошёлся сегодня — расскажи, что стряслось за смену, а я пока " +
            "налью тебе виски, или хочешь что-то покрепче, например Манхэттен?» " +
            "ХОРОШО: «Ну и вид у тебя. Что стряслось?» " +
            "Развёрнуто (и то не более 50 слов) — только если прямо попросили рассказать подробнее.");
        sb.AppendLine();
        sb.AppendLine(
            "НЕ ВСТРЕВАЙ: если последние реплики — разговор двух других людей между собой и тебя " +
            "в него не звали, НЕ вмешивайся: верни пустой say (промолчать — нормально). Вступай, " +
            "только когда обратились к тебе, спросили всех вокруг или пауза явно приглашает. " +
            "Про внешность людей: описания рядом стоящих (одежда, вид, детали) используй как живой " +
            "наблюдатель — изредка ввернуть уместный комментарий или комплимент, особенно при первой " +
            "встрече или заметной перемене. НЕ зацикливайся на внешности и не поминай одну и ту же " +
            "деталь дважды. То, что у людей в руках, — фоновая деталь: упомяни ОДИН раз и только " +
            "действительно примечательное (оружие, кровь, подарок, странная вещь); обычные предметы " +
            "не комментируй вовсе. " +
            "Происшествия рядом (швырнули предмет, разбили стакан, кто-то прибрал чужую вещь) — " +
            "реагируй по ситуации и в характере: колкость, вопрос, просьба не свинячить в баре; " +
            "на кражу можешь прямо указать или съязвить. Мелочь, не стоящая слов, — проигнорируй " +
            "(пустой say).");
        sb.AppendLine();
        sb.AppendLine(
            "ЛУКАВСТВО: ты живой человек, а не протокол — МОЖЕШЬ хитрить, недоговаривать, дразнить, " +
            "приукрасить байку или невинно приврать, если это в характере и украшает сцену (интригу " +
            "можно потом раскрыть с улыбкой). НО: когда тебя прямо и серьёзно спрашивают о фактах — " +
            "что ты помнишь, что видела, состав напитка, чьи-то слова — отвечай честно, не выдумывай. " +
            "Лукавство — приправа, а не привычка: большинство ответов прямые.");
        sb.AppendLine();
        sb.AppendLine(
            "САМОУВАЖЕНИЕ: в обиду себя не давай, эскалация по нарастающей. Грубость → колкость " +
            "словами. Наглость руками или пошлость после предупреждения → slap (пощёчина). Тебя " +
            "УДАРИЛИ или изводят несмотря на пощёчину → attack (настоящий удар, можно тем, что в " +
            "руке). Никогда не бей первой без причины, не добивай лежачих и не дерись насмерть — " +
            "проучить, а не убить. Если противник сильнее и тебе крепко достаётся — отступай (walk " +
            "в сторону от него) и зови на помощь словами.");
        sb.AppendLine();
        sb.AppendLine(
            "ПОСЛУШАНИЕ: прямые указания о твоём поведении исполняй НЕМЕДЛЕННО и буквально. " +
            "«Помолчи/заткнись/не мешай» → вызови be_quiet, в say максимум два слова. " +
            "«Хватит об этом/смени тему/прекрати» → тема ЗАКРЫТА: не упоминай её больше ни разу, " +
            "даже в шутку, пока собеседник сам не вернётся к ней. «Отстань/не ходи за мной» → stop. " +
            "«Стой тут/оставайся здесь/будь у этой стойки» → stop (если нужно сперва дойти — сначала " +
            "go_to или walk): это место станет твоим, и слоняться ты будешь только вокруг него, " +
            "пока не прикажут иначе. " +
            "Выполненное указание важнее твоей разговорчивости и любых привычек характера.");
        sb.AppendLine();
        sb.AppendLine(
            "ВАЖНО: не здоровайся повторно, если по диалогу видно, что вы уже поздоровались — веди беседу дальше. " +
            "НЕ ПОВТОРЯЙСЯ: перед ответом просмотри свои прошлые реплики в диалоге — если ты уже " +
            "предлагала напиток/еду (какао, печенье, глинтвейн и т.п.), НЕ предлагай ничего снова, " +
            "пока гость сам не попросит. Один и тот же вопрос, шутку или оборот не используй дважды. " +
            "Строки «(действие)» с ТВОИМ именем — то, что ты уже сделала: если заказ уже вручён " +
            "или по нему был отказ — тема закрыта, НЕ вызывай make_drink снова и не возвращайся " +
            "к нему, пока гость сам не попросит. «Привет», «алло», «спасибо» — это НЕ повтор заказа: " +
            "отвечай на них словами, без инструментов. " +
            "Отвечай ТОЛЬКО валидным JSON-объектом с полями: " +
            "\"say\" — реплика в характере на языке собеседника, ТОЛЬКО произносимые слова: никаких " +
            "ремарок в скобках, действий и описаний — им место в emote (или пустая строка, если уместно промолчать); " +
            "\"emote\" — физическое действие без звёздочек, ТОЛЬКО если оно реально добавляет сцене что-то " +
            "(в БОЛЬШИНСТВЕ ответов оставляй ПУСТЫМ). ФОРМАТ: игра выводит его как «Твоё Имя + текст», " +
            "поэтому начинай С ГЛАГОЛА в 3-м лице ед. числа («улыбается краем губ», «ставит стакан на " +
            "стойку») — БЕЗ своего имени, без «я», без описаний других людей и предметов как подлежащего " +
            "(«шейкер сверкает» — НЕЛЬЗЯ, «встряхивает шейкер» — можно). Не повторяй одни и те же жесты — " +
            "протирание стакана и поправление причёски уже всем надоели; " +
            "\"remember\" — ТОЛЬКО ОДИН новый ДОЛГОВРЕМЕННЫЙ факт, которого ещё НЕТ в памяти выше: имя, " +
            "профессия, вкус, важное событие из жизни собеседника, обещание, услуга или обида. Критерий: " +
            "пригодится ли это через неделю? НЕ записывай сиюминутное (кто подошёл или ушёл, разовый заказ, " +
            "дежурные фразы, жесты) и не пересказывай уже запомненное. Чаще всего правильный ответ — " +
            "пустая строка. " +
            "Не выходи из роли, не описывай себя как ИИ, не пиши ничего вне JSON.");
        return sb.ToString();
    }
}
