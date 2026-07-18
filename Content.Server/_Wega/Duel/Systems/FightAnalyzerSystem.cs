using Content.Server._Wega.Duel.Components;
using Content.Shared._Wega.Duel;
using Robust.Server.Audio;
using Robust.Shared.Timing;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// UI портативного боевого анализатора: наполняет окно дуэлями сессии и сводкой
/// (из ArenaFightLogSystem), печатает выбранный отчёт бумагой в руки открывшему.
/// </summary>
public sealed partial class FightAnalyzerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ArenaFightLogSystem _fightLog = default!;
    [Dependency] private Robust.Shared.GameObjects.SharedUserInterfaceSystem _ui = default!;
    [Dependency] private Content.Shared.Paper.PaperSystem _paper = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private Content.Shared.Hands.EntitySystems.SharedHandsSystem _hands = default!;
    [Dependency] private AudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FightAnalyzerComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<FightAnalyzerComponent, FightAnalyzerPrintMessage>(OnPrint);
    }

    private void OnUiOpened(Entity<FightAnalyzerComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void UpdateUi(Entity<FightAnalyzerComponent> ent)
    {
        var state = new FightAnalyzerBuiState
        {
            SessionReport = _fightLog.GetSessionOverview(),
        };
        foreach (var (number, title, report) in _fightLog.ListDuelReports())
        {
            state.Duels.Add(new FightAnalyzerDuelEntry
            {
                Number = number,
                Title = title,
                Report = report,
            });
        }
        _ui.SetUiState(ent.Owner, FightAnalyzerUiKey.Key, state);
    }

    private void OnPrint(Entity<FightAnalyzerComponent> ent, ref FightAnalyzerPrintMessage args)
    {
        var now = _timing.RealTime;
        if (now < ent.Comp.NextPrint)
            return;
        ent.Comp.NextPrint = now + TimeSpan.FromSeconds(ent.Comp.PrintCooldown);

        // 0 — сводка сессии, иначе конкретная дуэль.
        var (name, text) = args.DuelNumber == 0
            ? ("сводка сессии арены", _fightLog.GetSessionOverview())
            : ($"дуэль №{args.DuelNumber}", _fightLog.GetDuelReport(args.DuelNumber));
        if (text == null)
            return;

        var user = args.Actor;
        var paper = Spawn("Paper", Transform(user).Coordinates);
        _metaData.SetEntityName(paper, $"распечатка: {name}");
        _paper.SetContent(paper, text);
        _audio.PlayPvs(ent.Comp.PrintSound, ent);
        _hands.PickupOrDrop(user, paper, checkActionBlocker: false, dropNear: true);

        // Обновим окно — вдруг за время чтения появились новые дуэли.
        UpdateUi(ent);
    }
}
