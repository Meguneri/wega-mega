using Content.Server.GameTicking;
using Content.Shared._Wega.Magic;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Blood.Cult.Components;
using Content.Shared.Magic.Components;
using Content.Shared.GameTicking;

namespace Content.Server._Wega.GameTicking;

/// <summary>
/// Чистка «следов» после завершения раунда (переход в <see cref="GameRunLevel.PostRound"/>).
/// Нужно для арены/персистентных тел, где сущности не удаляются сразу:
/// <list type="bullet">
///   <item>Снимает с игроков магические способности мага — любое action со <see cref="MagicComponent"/>.</item>
///   <item>Удаляет нарисованные руны культа (<see cref="BloodRuneComponent"/>).</item>
///   <item>Удаляет магические руны со свитка рун (<see cref="MagicRuneComponent"/>).</item>
/// </list>
/// </summary>
public sealed partial class PostRoundPurgeSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        // Реагируем именно на момент завершения раунда.
        if (ev.New != GameRunLevel.PostRound)
            return;

        PurgeMagicActions();
        PurgeRunes();
        PurgeMagicRunes();
    }

    /// <summary>
    /// Снимает все магические действия (спеллы), привязанные к какому-либо владельцу.
    /// Спелл-действия несут <see cref="MagicComponent"/> вместе с <see cref="ActionComponent"/>.
    /// </summary>
    private void PurgeMagicActions()
    {
        var query = EntityQueryEnumerator<MagicComponent, ActionComponent>();
        while (query.MoveNext(out var uid, out _, out var action))
        {
            if (action.AttachedEntity is null)
                continue;

            _actions.RemoveAction((uid, action));
        }
    }

    /// <summary>
    /// Удаляет все руны культа, нарисованные на полу.
    /// </summary>
    private void PurgeRunes()
    {
        var query = EntityQueryEnumerator<BloodRuneComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            QueueDel(uid);
        }
    }

    /// <summary>
    /// Удаляет все магические руны со свитка рун (FlashRune/ExplosionRune/IgniteRune/StunRune
    /// и т.п.), оставшиеся на карте после раунда.
    /// </summary>
    private void PurgeMagicRunes()
    {
        var query = EntityQueryEnumerator<MagicRuneComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            QueueDel(uid);
        }
    }
}
