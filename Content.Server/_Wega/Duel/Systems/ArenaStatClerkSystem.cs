using Content.Server._Wega.Duel.Components;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Preferences;
using Robust.Server.Audio;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Статистик арены без LLM: кликнули рукой — он стучит по анализатору (звук печати) и через пару
/// секунд вручает кликнувшему бумажную распечатку его последней дуэли (GetPaperReport). Дуэлей
/// нет — отшивает дежурной фразой. Весь «интеллект» — детерминированный код, ноль токенов.
/// </summary>
public sealed partial class ArenaStatClerkSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private ArenaFightLogSystem _fightLog = default!;
    [Dependency] private Content.Shared.Paper.PaperSystem _paper = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private Content.Shared.Hands.EntitySystems.SharedHandsSystem _hands = default!;
    [Dependency] private Content.Shared.Interaction.RotateToFaceSystem _rotateToFace = default!;
    [Dependency] private Robust.Server.GameObjects.TransformSystem _transform = default!;
    [Dependency] private HumanoidProfileSystem _humanoidProfile = default!;
    [Dependency] private Content.Shared.Body.SharedVisualBodySystem _visualBody = default!;
    [Dependency] private Content.Shared.Mobs.Systems.MobStateSystem _mobState = default!;

    private static readonly string[] DoneLines =
    {
        "arena-stat-clerk-done-1",
        "arena-stat-clerk-done-2",
        "arena-stat-clerk-done-3",
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ArenaStatClerkComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ArenaStatClerkComponent, InteractHandEvent>(OnInteractHand);
    }

    /// <summary>
    /// Внешность и косметика на спавне — как у LLM-NPC, но без мозга: убираем «Zz»/«в КРС»,
    /// генерим случайный мужской профиль (имя остаётся из прототипа), причёску.
    /// </summary>
    private void OnMapInit(Entity<ArenaStatClerkComponent> ent, ref MapInitEvent args)
    {
        RemComp<Content.Shared.SSDIndicator.SSDIndicatorComponent>(ent);
        RemComp<Content.Shared.Mind.Components.MindExaminableComponent>(ent);

        if (!TryComp<HumanoidProfileComponent>(ent, out var humanoid))
            return;

        var profile = HumanoidCharacterProfile.RandomWithSpecies(humanoid.Species)
            .WithSex(Sex.Male)
            .WithGender(Robust.Shared.Enums.Gender.Male);
        _visualBody.ApplyProfileTo(ent.Owner, profile);
        _humanoidProfile.ApplyProfileTo(ent.Owner, profile);

        // Аккуратная короткая причёска конторского служащего.
        var markings = new Dictionary<Robust.Shared.Prototypes.ProtoId<Content.Shared.Body.OrganCategoryPrototype>,
            Dictionary<HumanoidVisualLayers, System.Collections.Generic.List<Content.Shared.Humanoid.Markings.Marking>>>
        {
            ["Head"] = new()
            {
                [HumanoidVisualLayers.Hair] = new()
                {
                    new Content.Shared.Humanoid.Markings.Marking("HumanHairBusiness", new[] { Color.FromHex("#4a3826") }),
                },
            },
        };
        _visualBody.ApplyMarkings(ent.Owner, markings);
    }

    private void OnInteractHand(Entity<ArenaStatClerkComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled || _mobState.IsIncapacitated(ent))
            return;
        args.Handled = true;

        var user = args.User;
        var now = _timing.RealTime;

        // Уже обслуживает — пусть подождут очереди.
        if (ent.Comp.PendingUser != null)
            return;

        if (ent.Comp.LastServed.TryGetValue(user, out var last)
            && now - last < TimeSpan.FromSeconds(ent.Comp.Cooldown))
            return;
        ent.Comp.LastServed[user] = now;

        _rotateToFace.TryFaceCoordinates(ent, _transform.GetWorldPosition(user));
        _audio.PlayPvs(ent.Comp.TypingSound, ent);
        _chat.TrySendInGameICMessage(ent, Loc.GetString("arena-stat-clerk-typing"),
            InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);

        ent.Comp.PendingUser = user;
        ent.Comp.PrintAt = now + TimeSpan.FromSeconds(ent.Comp.PrintDelay);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.RealTime;
        var query = EntityQueryEnumerator<ArenaStatClerkComponent>();
        while (query.MoveNext(out var uid, out var clerk))
        {
            if (clerk.PendingUser is not { } user || now < clerk.PrintAt)
                continue;
            clerk.PendingUser = null;

            if (!Exists(user) || TerminatingOrDeleted(user))
                continue;

            var report = _fightLog.GetPaperReport(user);
            if (report == null)
            {
                _chat.TrySendInGameICMessage(uid, Loc.GetString("arena-stat-clerk-empty"),
                    InGameICChatType.Speak, ChatTransmitRange.Normal);
                continue;
            }

            var paper = Spawn("Paper", Transform(uid).Coordinates);
            _metaData.SetEntityName(paper, Loc.GetString("arena-stat-clerk-paper-name",
                ("name", MetaData(user).EntityName)));
            _paper.SetContent(paper, report);

            _chat.TrySendInGameICMessage(uid, Loc.GetString("arena-stat-clerk-prints"),
                InGameICChatType.Emote, ChatTransmitRange.Normal, ignoreActionBlocker: true);

            // Вручаем прямо в руки; руки заняты — лист ляжет рядом с ним.
            _hands.PickupOrDrop(user, paper, checkActionBlocker: false, dropNear: true);

            // Дежурная фраза + сухая выжимка цифрами — всё, что он готов сказать вслух.
            var line = Loc.GetString(_random.Pick(DoneLines));
            if (_fightLog.GetVoiceSummary(user) is { } summary)
                line = $"{line} {summary}";
            _chat.TrySendInGameICMessage(uid, line, InGameICChatType.Speak, ChatTransmitRange.Normal);
        }
    }
}
