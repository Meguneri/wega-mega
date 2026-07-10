# CLAUDE.md

Guidance for working in this repository (a fork of ss14-wega / Space Station 14).

## RepoWise first

RepoWise (the MCP server documented in `.claude/CLAUDE.md`) is the **primary source of truth** about this project. Use it before reading source.

- Prefer RepoWise's served bytes (`get_context` skeletons, `get_symbol` bodies) over a raw `Read`, and avoid mass file reads / repo-wide `grep`/`find` when RepoWise already has the answer.
- RepoWise has priority **but may be stale** — its index is pinned to a commit, so uncommitted or newer changes may not be reflected. When RepoWise contradicts the working tree, **trust the working tree.** Re-verify against it on any `stale_warning`, `bounds: approximate`, `confidence: low`, or when the file is uncommitted.

## Analysis workflow

Before analysis:

1. Use RepoWise to get the architecture and dependencies (`get_overview`, `get_answer`, `get_context`, `get_risk`, `get_why`).
2. From what it returns, determine the **minimal set of files** needed for the task.
3. Justify why each file in that set is needed. **If more than 5 files are required, explain why before reading them.**

During analysis:

4. After each file, re-evaluate whether the next one is still needed.
5. Stop as soon as the hypothesis is confirmed — do not keep reading.
6. Never read files "just in case" / "for confidence".
7. Every **High-severity** conclusion must be independently re-verified against the working tree (not just RepoWise).

## Parallel agents

Do not spawn multiple agents automatically. Use parallel agents only when **all** of these hold:

- the subsystems are genuinely independent;
- parallelism actually reduces wall-clock time;
- the analysis volume is genuinely large.

Otherwise work with a single agent. Optimise for solving the stated task with the **minimum** actions — do not chase maximal coverage for its own sake.

## Dependency injection (RA0049 / RA0051)

Types with `[Dependency]` fields must be `partial`, and `[Dependency]` fields must **not** be `readonly`.

```csharp
public sealed partial class MySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;   // no readonly
}
```

Applies to `EntitySystem`s, `IConsoleCommand`s, `Overlay`s, and anything else using `[Dependency]`.

## Arsenal pools

When you touch an arsenal pool, keep these in sync:

- **Full Arsenal** — `full_arsenal_pool.yml` ↔ `FULL_ARSENAL_PRICES.md` (name, entity id, TC cost per category).
- **Melee Arsenal** — `melee_arsenal_pool.yml` ↔ `MELEE_ARSENAL_PRICES.md`. Any melee/shield/armor item added to Full Arsenal must also go in the Melee pool and both price lists.
- **ru-RU** — every Full Arsenal item needs a Russian name and description: the listing keys (`full-arsenal-*-name` / `-desc`) and the entity (`ent-<EntityId>`). Ported weapons keep their model designation (e.g. `АС-12 «Минотавр»`) but still get a `ru-RU` entry so nothing falls back to English.
