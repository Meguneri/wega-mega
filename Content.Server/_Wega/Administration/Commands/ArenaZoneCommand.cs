using System.Collections.Generic;
using Content.Server._Wega.Duel.Components;
using Content.Server._Wega.Duel.Systems;
using Content.Shared._Wega.Duel;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.Server.Administration.Commands;

/// <summary>
/// Управление «зоной» дуэльной арены из консоли: включает/выключает шторм и/или авиаудары
/// на арене, ближайшей к игроку, введшему команду (или на всех аренах, если введена с серверной консоли).
/// <c>arenazone off</c> — отключить; <c>arenazone on</c> — включить.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class ArenaZoneCommand : IConsoleCommand
{
    [Dependency] private IEntitySystemManager _sysMan = default!;
    [Dependency] private IEntityManager _entityManager = default!;

    public string Command => "arenazone";
    public string Description => Loc.GetString("cmd-arenazone-desc");
    public string Help => Loc.GetString("cmd-arenazone-help", ("command", Command));

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("cmd-arenazone-invalid-args", ("command", Command)));
            return;
        }

        var arg = args[0].ToLowerInvariant();
        if (arg != "off" && arg != "on")
        {
            shell.WriteError(Loc.GetString("cmd-arenazone-invalid-args", ("command", Command)));
            return;
        }

        var enabled = arg == "on";
        var stormSys = _sysMan.GetEntitySystem<ArenaStormSystem>();
        var airstrikeSys = _sysMan.GetEntitySystem<ArenaAirstrikeSystem>();
        var transformSys = _sysMan.GetEntitySystem<SharedTransformSystem>();

        var targets = new List<EntityUid>();

        // Если команду ввёл игрок — применяем к ближайшей арене на его карте.
        if (shell.Player?.AttachedEntity is { } playerEntity)
        {
            var playerCoords = transformSys.GetMapCoordinates(playerEntity);
            EntityUid nearest = EntityUid.Invalid;
            var nearestDistSq = float.MaxValue;

            var query = _entityManager.EntityQueryEnumerator<DuelArenaComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                var arenaCoords = transformSys.GetMapCoordinates(uid);
                if (arenaCoords.MapId != playerCoords.MapId)
                    continue;

                var distSq = (arenaCoords.Position - playerCoords.Position).LengthSquared();
                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearest = uid;
                }
            }

            if (nearest == EntityUid.Invalid)
            {
                shell.WriteError(Loc.GetString("cmd-arenazone-no-arena"));
                return;
            }

            targets.Add(nearest);
        }
        else
        {
            // Серверная консоль: применяем ко всем аренам.
            var query = _entityManager.EntityQueryEnumerator<DuelArenaComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                targets.Add(uid);
            }
        }

        var stormCount = 0;
        var airstrikeCount = 0;

        foreach (var uid in targets)
        {
            if (_entityManager.HasComponent<ArenaStormComponent>(uid))
            {
                stormSys.ToggleStorm(uid, enabled);
                stormCount++;
            }

            if (_entityManager.HasComponent<ArenaAirstrikeComponent>(uid))
            {
                airstrikeSys.ToggleAirstrike(uid, enabled);
                airstrikeCount++;
            }
        }

        if (enabled)
            shell.WriteLine(Loc.GetString("cmd-arenazone-on-result", ("storm", stormCount), ("airstrike", airstrikeCount)));
        else
            shell.WriteLine(Loc.GetString("cmd-arenazone-off-result", ("storm", stormCount), ("airstrike", airstrikeCount)));
    }
}
