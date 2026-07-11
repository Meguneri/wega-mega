using System;
using System.Collections.Generic;
using Content.Server._Wega.Duel.Components;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.Administration.Commands;

/// <summary>
/// Задаёт тир арсенал-крейта (FullArsenal <c>SurplusBundle</c>), который спавнится у спавн-маркеров
/// каждой арены при старте раунда. Настройка применяется ко ВСЕМ аренам и полностью заменяет прежнюю —
/// никакого наложения тиров: <c>arenaarsenal CrateSyndicateFullArsenal</c> сменит тир на 40 ТК, даже
/// если раньше стоял 120 ТК. <c>arenaarsenal off</c> — отключить спавн крейтов.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class ArenaArsenalCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IComponentFactory _factory = default!;

    [ValidatePrototypeId<EntityPrototype>]
    private const string RemoteProto = "ArenaArsenalRemote";

    public string Command => "arenaarsenal";
    public string Description => Loc.GetString("cmd-arenaarsenal-desc");
    public string Help => Loc.GetString("cmd-arenaarsenal-help", ("command", Command));

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("cmd-arenaarsenal-invalid-args", ("command", Command)));
            return;
        }

        var arg = args[0];
        EntProtoId? crate;

        if (arg.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            crate = null;
        }
        else
        {
            // Разрешаем только настоящие арсенал-крейты (SurplusBundle), чтобы опечатка не начала
            // спавнить произвольную сущность у каждого маркера каждый раунд.
            if (!_prototype.TryIndex<EntityPrototype>(arg, out var proto)
                || !proto.Components.ContainsKey("SurplusBundle"))
            {
                shell.WriteError(Loc.GetString("cmd-arenaarsenal-bad-crate", ("crate", arg)));
                return;
            }

            crate = arg;
        }

        var count = 0;
        var query = _entityManager.EntityQueryEnumerator<DuelArenaComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            comp.ArsenalCrate = crate;
            count++;
        }

        if (crate == null)
            shell.WriteLine(Loc.GetString("cmd-arenaarsenal-off-result", ("count", count)));
        else
            shell.WriteLine(Loc.GetString("cmd-arenaarsenal-set-result", ("crate", arg), ("count", count)));
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        var options = new List<CompletionOption> { new("off", "отключить спавн крейтов") };

        if (_prototype.TryIndex<EntityPrototype>(RemoteProto, out var remoteProto)
            && remoteProto.TryGetComponent<ArenaArsenalRemoteComponent>(out var remote, _factory))
        {
            foreach (var c in remote.Crates)
            {
                var name = _prototype.TryIndex<EntityPrototype>(c, out var p) ? p.Name : c.Id;
                options.Add(new CompletionOption(c.Id, name));
            }
        }

        return CompletionResult.FromHintOptions(options, "<прототип-крейта | off>");
    }
}
