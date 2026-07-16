using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Wega.LlmNpc;

/// <summary>
/// Собирает «портрет стиля» из логов реального игрока и сохраняет его для NPC-компаньона с указанным
/// файлом памяти. Тратит запрос к API и пишет в data-папку, поэтому только для хостов.
/// Использование: llmpersona &lt;memoryFile&gt; &lt;файл_логов&gt;  (например: llmpersona bartender ivan.txt)
/// </summary>
[AdminCommand(AdminFlags.Host)]
public sealed partial class LlmPersonaCommand : LocalizedEntityCommands
{
    [Dependency] private LlmNpcSystem _llm = default!;

    public override string Command => "llmpersona";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError("Использование: llmpersona <memoryFile> <файл_логов>  (напр.: llmpersona bartender ivan.txt)");
            return;
        }

        _llm.DistillPersona(args[0], args[1], shell);
    }
}
