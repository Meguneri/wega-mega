using Content.Shared._Wega.Arena.Cold;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.Server.Administration.Commands;

/// <summary>
/// Enables or disables the Frostpunk-style cold on the grid occupied by the invoking player.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class ArenaColdCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entityManager = default!;

    public string Command => "arenacold";
    public string Description => Loc.GetString("cmd-arenacold-desc");
    public string Help => Loc.GetString("cmd-arenacold-help", ("command", Command));

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("cmd-arenacold-invalid-args", ("command", Command)));
            return;
        }

        var arg = args[0].ToLowerInvariant();
        if (arg != "on" && arg != "off")
        {
            shell.WriteError(Loc.GetString("cmd-arenacold-invalid-args", ("command", Command)));
            return;
        }

        if (shell.Player?.AttachedEntity is not { } playerEntity)
        {
            shell.WriteError(Loc.GetString("cmd-arenacold-player-only"));
            return;
        }

        var transform = _entityManager.GetComponent<TransformComponent>(playerEntity);
        if (transform.GridUid is not { } grid)
        {
            shell.WriteError(Loc.GetString("cmd-arenacold-no-grid"));
            return;
        }

        // Зона накрывает ВЕСЬ грид, а не только арену вокруг тебя — поэтому всегда называем,
        // какой именно грид зацепили. Иначе легко «включить холод» стоя на станции.
        var gridName = _entityManager.ToPrettyString(grid);

        if (arg == "on")
        {
            if (_entityManager.HasComponent<ArenaColdZoneComponent>(grid))
            {
                shell.WriteLine(Loc.GetString("cmd-arenacold-on-already", ("grid", gridName)));
                return;
            }

            _entityManager.EnsureComponent<ArenaColdZoneComponent>(grid);
            shell.WriteLine(Loc.GetString("cmd-arenacold-on-result", ("grid", gridName)));
            return;
        }

        if (!_entityManager.RemoveComponent<ArenaColdZoneComponent>(grid))
        {
            shell.WriteLine(Loc.GetString("cmd-arenacold-off-already", ("grid", gridName)));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-arenacold-off-result", ("grid", gridName)));
    }
}
