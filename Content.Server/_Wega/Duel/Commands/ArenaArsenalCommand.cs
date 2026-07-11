using System.Linq;
using Content.Server._Wega.Duel.Components;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._Wega.Duel.Commands;

/// <summary>
/// Админская команда: задаёт тир арсенал-крейта на всех аренах дуэлей — то же самое, что делает
/// <see cref="Systems.ArenaArsenalRemoteSystem"/> через пульт <c>ArenaArsenalRemote</c>.
/// Никакого конфликта с пультом: оба просто пишут <see cref="DuelArenaComponent.ArsenalCrate"/>,
/// побеждает последняя запись (как если бы два пульта нажали по очереди). Аргумент <c>off</c> —
/// отключить спавн крейтов.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class ArenaArsenalCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IComponentFactory _factory = default!;

    /// <summary>Прототип пульта — источник канонического списка тиров для автодополнения.</summary>
    private const string RemoteProto = "ArenaArsenalRemote";

    public string Command => "arenaarsenal";
    public string Description => "Задаёт тир арсенал-крейта на всех аренах дуэлей (как пульт арсенала). 'off' — отключить спавн.";
    public string Help => $"Использование: {Command} <id крейта|off>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError($"Неверное число аргументов.\n{Help}");
            return;
        }

        EntProtoId? crate = null;
        if (!string.Equals(args[0], "off", StringComparison.OrdinalIgnoreCase))
        {
            if (!_proto.HasIndex<EntityPrototype>(args[0]))
            {
                shell.WriteError($"Неизвестный прототип крейта: {args[0]}");
                return;
            }

            crate = args[0];
        }

        var count = 0;
        var query = _entManager.EntityQueryEnumerator<DuelArenaComponent>();
        while (query.MoveNext(out _, out var arena))
        {
            arena.ArsenalCrate = crate;
            count++;
        }

        if (count == 0)
        {
            shell.WriteLine("На карте нет арен дуэлей (DuelArenaComponent).");
            return;
        }

        var tier = crate is { } c
            ? (_proto.TryIndex<EntityPrototype>(c, out var p) ? p.Name : c.Id)
            : "отключено";
        shell.WriteLine($"Арсенал «{tier}» применён к аренам: {count}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        var options = new List<CompletionOption> { new("off", "отключить спавн крейтов") };

        // Тянем тиры из компонента пульта, чтобы список команды и пульта не расходился.
        if (_proto.TryIndex<EntityPrototype>(RemoteProto, out var remoteProto)
            && remoteProto.TryGetComponent<ArenaArsenalRemoteComponent>(out var remote, _factory))
        {
            foreach (var c in remote.Crates)
            {
                var name = _proto.TryIndex<EntityPrototype>(c, out var p) ? p.Name : c.Id;
                options.Add(new CompletionOption(c.Id, name));
            }
        }

        return CompletionResult.FromHintOptions(options, "<id крейта|off>");
    }
}
