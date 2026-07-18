using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Wega.LlmNpc;

/// <summary>
/// Показывает расход API LLM-NPC за текущий раунд: запросы, точные токены (из usage-блоков
/// ответов провайдера, включая кэшированные) и стоимость по прайсу wega.llm_npc_prices,
/// с разбивкой по «NPC | ключ | модель». Использование: llmnpc_usage
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class LlmNpcUsageCommand : LocalizedEntityCommands
{
    [Dependency] private LlmNpcSystem _llm = default!;

    public override string Command => "llmnpc_usage";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        shell.WriteLine(_llm.UsageReport());
    }
}
