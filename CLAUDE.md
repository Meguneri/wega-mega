# CLAUDE.md

Этот файл — руководство для Claude Code (claude.ai/code) при работе с репозиторием. `AGENTS.md` — зеркало этого файла для Codex: осмысленные правки вноси в оба файла.

**wega-mega** — форк [ss14-wega](https://github.com/corvax-team/ss14-wega) (Corvax → Space Station 14), русскоязычный сервер «Wega». C#/.NET на движке RobustToolbox, YAML-прототипы, локализация на Fluent (ftl). Основная рабочая ветка — `arena-mode-develop`. Документация и коммиты в репозитории — на русском; XML-доки в коде традиционно на английском.

## Стек и конфигурация

- **.NET SDK 10.0.100** (`global.json`, rollForward `latestFeature`), `LangVersion 14`, `Nullable enable` (`MSBuild/Content.props`).
- Решение — `SpaceStation14.slnx` (XML-формат). Конфигурации: `Debug`, `DebugOpt`, `Release`, `Tools`. В `Release` включён `TreatWarningsAsErrors` (со списком исключений `WarningsNotAsErrors`).
- Центральные версии пакетов — `Directory.Packages.props` (импортирует `RobustToolbox/Directory.Packages.props`): EF Core (Npgsql + Sqlite), NetCord, Veldrid, OpenTK, ImGui.NET, CsvHelper. Фиды NuGet — `nuget.config` (nuget.org + dotnet-eng).
- `RUN_THIS.py` — инициализация сабмодулей и скачивание движка после клонирования.
- `flake.nix` + `shell.nix` + `.envrc` (nix-direnv) — девшелл для Nix.
- `run-*.sh` / `run-*.bat` в корне — обёртки над `dotnet build/run` для типовых сценариев (debug/release, client/server, tools).

## Команды

```bash
# Сборка (вывод локализован на русский: «Ошибок: 0» = 0 ошибок)
dotnet build Content.Server/Content.Server.csproj -c Debug
dotnet build Content.Client/Content.Client.csproj -c Debug

# Локальный запуск (сначала сервер, потом клиент; клиент подключается к localhost)
dotnet run --project Content.Server
dotnet run --project Content.Client

# Валидация YAML-прототипов (тяжёлая: грузит весь контент)
dotnet run --project Content.YAMLLinter

# Тесты
dotnet test Content.Tests                               # юнит
dotnet test Content.IntegrationTests                    # интеграционные (медленно)
dotnet test Content.Tests --filter "FullyQualifiedName~SomeTestName"
```

- **RobustToolbox — git-сабмодуль.** Изменения движка коммиться *внутри* `RobustToolbox/` с последующим поднятием указателя сабмодуля в основном репо, иначе они потеряются. В `.gitmodules` стоит `ignore = dirty` — «грязное» состояние сабмодуля в `git status` скрыто намеренно.
- Схема БД — EF Core миграции под оба провайдера в `Content.Server.Database/Migrations/{Postgres,Sqlite}`; добавление — через `Content.Server.Database/add-migration.sh`.

## Структура репозитория

Классическое для SS14 разделение на три сборки:

- **Content.Shared** — компоненты, события, cvar'ы, общие для клиента и сервера. Сетевые события — `[Serializable, NetSerializable]` классы.
- **Content.Server** — авторитетные системы (`EntitySystem`), консольные команды (`[AdminCommand(AdminFlags.X)]` + `LocalizedEntityCommands`).
- **Content.Client** — UI (XAML + code-behind, `[GenerateTypedNameReferences]`), клиентские системы, оверлеи.

Вспомогательные проекты: `Content.Server.Database` / `Content.Shared.Database` (EF Core, Postgres/SQLite), `Content.Tests`, `Content.IntegrationTests`, `Content.MapRenderer`, `Content.Packaging`, `Content.Replay`, `Content.Benchmarks`, `Content.YAMLLinter`, `Content.Tools`, `Content.PatreonParser`, `Content.Docfx`, `BuildChecker`, `Pow3r`, `Corvax/Content.Corvax.Interfaces.*`.

### Форк-дисциплина (`_Wega`)

Весь код и контент форка живёт в подкаталогах `_Wega`: `Content.{Client,Server,Shared}/_Wega/`, `Resources/Prototypes/_Wega/`, `Resources/Textures/_Wega/`, `Resources/Locale/{ru-RU,en-US}/_wega/`, `Resources/Maps/_Wega/`. Каталоги `Corvax/` и `*/Corvax/` — апстрим-контент Corvax; система спонсоров здесь только интерфейсами (`Corvax/Content.Corvax.Interfaces.*`) — реализация закрыта и отсутствует, так что спонсорский контент в рантайме недостижим. Апстрим-файлы (вне `_Wega`) не трогай без необходимости.

Контент, портированный из других форков, лежит в префиксных каталогах `_Sunrise`, `_Starlight`, `_RMC14`, `_Lust` — лицензии и авторство исходных проектов сохраняются (см. `meta.json` и заголовки файлов), реестр — в `Resources/Prototypes/_Wega/EXTERNAL_CONTENT.md`.

### Папка `dev/`

Служебные файлы форка (не нужны игровой сборке): генераторы спрайтов/параллакса (`gen_adaptive_sprites.py`, `gen_rengoku_inhand.py`, `gen_urban_parallax.py` — запускать из корня репо, пишут в `Resources/Textures/`), запуск маппинга (`run_mapping.sh`, `MAPPING_README.txt`), сборка standalone-билда под Windows (`package_windows_standalone.sh` → `dist/`), референсы человеческой модельки, заметки по механикам (`TODO.md`, `RAID_MODE.md`, `DUEL_SUPPLY_DROP.md`). Часть генераторов спрайтов лежит и в `Tools/gen_*.py` — см. ниже.

## Архитектура

### Песочница клиента — сборка проходит, клиент падает

Client и Shared сборки проверяются IL-вайтлистом при *старте* (`RobustToolbox/Robust.Shared/ContentPack/Sandbox.yml`). `dotnet build` нарушения НЕ ловит; клиент умирает на запуске с `Sandbox violation`. Известные грабли:

- ImageSharp `Image.Load*` запрещён на клиенте — PNG декодировать через `IClyde.LoadTextureFromPNGStream`; вайтлистнуты лишь несколько методов ImageSharp `Processing`.
- Позиционный звук (`AudioSystem.PlayEntity`) ассертит на стерео-потоках — серверный ffmpeg обязан выдавать **моно** (`-ac 1`) для звуков, привязанных к сущностям.

### Внедрение зависимостей (RA0049 / RA0051)

Типы с `[Dependency]`-полями должны быть `partial`, а `[Dependency]`-поля — **не** `readonly`.

```csharp
public sealed partial class MySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;   // без readonly
}
```

Относится к `EntitySystem`, `IConsoleCommand`, `Overlay` и всему, где есть `[Dependency]`.

### Прототипы, спрайты, локализация

- Имена/описания сущностей берутся из ключей `ent-<EntityId>` в ftl (с атрибутом `.desc`); для новых сущностей не пиши loc-ключи в поле `name:` yml-прототипа. Прототипы тайлов, наоборот, *используют* loc-ключи в `name:`.
- Каждой фиче `_Wega` нужны **обе** локали: `ru-RU` (основная) и `en-US`.
- Спрайты — RSI-папки (`*.rsi/meta.json` + PNG). Кадры анимации живут в одном спрайт-листе на стейт, тайминги — в `delays` (внешний массив = направления, внутренний = кадры). IconSmooth-стены = `full` + 8 стейтов × 4 направления-квадранта — добавляя стены, перекрашивай геометрию существующего RSI, а не рисуй стейты вручную. Напольные тайлы — листы 128×32 = 4 варианта (`variants: 4`).
- Многие спрайты `_Wega` генерируются скриптами `Tools/gen_*.py` и `dev/gen_*.py` (Python + PIL): правь генератор и перезапускай его, PNG руками не редактируй. После любой работы со спрайтами покажи пользователю увеличенный превью-монтаж. Если системный python виснет на `from PIL import Image` (проверка Gatekeeper на macOS) — используй venv `dev/.venv-sprites` (homebrew-python + pillow, рецепт в `.gitignore`).
- `.rsi` регистрируется как один `RSIResource` — `xe:Tex`/`GetTexture` не загрузит голый PNG изнутри `.rsi`; отдельные UI-текстуры должны лежать вне `.rsi`-папок.

## Подсистемы форка

- **Арена / дуэли** — `Content.Server/_Wega/Duel/`; обзор в `ARENA_MODE.md`. Счёт/серии — `DuelArenaScoreSystem`; уборка (`DuelArenaCleanupSystem`), восстановление (`DuelArenaRestoreSystem`), ротация (`DuelRotationSystem`), готовность (`DuelReadySystem`), арсенал-пульт (`ArenaArsenalRemoteSystem`), шторм (`ArenaStormSystem`) — отдельные системы. Киберпанк-тайлы/стены арены генерируются `Tools/gen_arena_cyberpunk_pack2.py`.
- **Рейд (экстракшн, PvEvE)** — `Content.{Server,Shared}/_Wega/Raid/`; документация: `dev/RAID_MODE.md` и раздел в `README.md`. Тарковский кор-луп: личная база (`hideout.yml`, отдельный `MapId` на каждый `NetUserId`) → закупка в магазине за ТК → вход в рейд → лут с меткой `RaidLoot` → экстракт по таймеру; смерть/MIA = потеря найденного. Стэш, валюта и статистика персистентны в БД сервера (`RaidStashSystem`). С ареной-модом не пересекается: разные карты и контроллеры. Быстрый тест: `dotnet test Content.IntegrationTests --filter "FullyQualifiedName~_Wega.Raid.RaidControllerTest"`.
- **LLM-NPC «Ева»** — `Content.Server/_Wega/LlmNpc/`; полный README с архитектурой, cvar'ами, граблями и деплоем: `Content.Server/_Wega/LlmNpc/README.md`. NPC на OpenAI-совместимом API: слух/зрение, tool-calling (коктейли из реальных запасов бара, ходьба, вручение), файловая память с консолидацией. Ключ API — только в untracked `server_config.toml` (cvar `llm_npc_api_key` — SERVERONLY+CONFIDENTIAL).
- **Медиаплеер + ТВ** — `Content.{Client,Server,Shared}/_Wega/MediaPlayer/`, `Content.Shared/_Wega/TvScreen/`. Сервер качает через yt-dlp + ffmpeg (автоустановка в user data dir), рассылает ogg/PNG-кадры сетевыми событиями; доступ админский (`AdminFlags.Fun`). ТВ-видео = PNG-кадры на динамическом спрайт-слое, моно-позиционный звук на каждый экран.

## Тестирование

- `Content.Tests` — юнит-тесты; `Content.IntegrationTests` — интеграционные (пул сервер+клиент, медленные).
- Тесты форка лежат в `Content.IntegrationTests/Tests/_Wega/`: арена (`Arena101x101Test`, `ArenaMapsLoadTest`, `ArenaPunisherTest`), дуэли (`Duel/`), рейд (`Raid/RaidControllerTest`), медиаплеер, оружие (`Weapons/Rengoku/`).
- Фильтрация: `dotnet test Content.IntegrationTests --filter "FullyQualifiedName~_Wega.Raid.RaidControllerTest"`.
- Перед коммитом минимум: сборка Server+Client и `Content.YAMLLinter`, если трогал прототипы.

## CI

В `.github/workflows/` лежат воркфлоу, унаследованные от апстрима: `build-test-debug.yml` (сборка и тесты на ubuntu-latest с поднятием сабмодулей), `yaml-linter.yml`, `validate-rsis.yml`, `validate_mapfiles.yml`, `check-crlf.yml`, `test-packaging.yml`, `benchmarks.yml` и др. Их триггеры настроены на ветки `master`/`staging`/`stable`, так что на рабочей ветке `arena-mode-develop` они не запускаются — локальная проверка сборкой и тестами обязательна.

## Деплой

- `DEPLOY.md` намеренно в `.gitignore` (русскоязычный runbook с инфраструктурными деталями приватного сервера) — никогда не форс-аддь его.
- Деплой — VPS, запуск из исходников под systemd; `server_config.toml` там untracked и переживает `git reset --hard`. Имя cvar'а `section.key` маппится в TOML `[section]` + `key`; дублирующиеся заголовки таблиц — синтаксическая ошибка TOML.
- Standalone-сборка клиента под Windows: `./dev/package_windows_standalone.sh`, результат в `dist/`.

## Реестр внешнего контента

Портируя контент из другого SS14-репозитория в `_Wega`, запиши его в `Resources/Prototypes/_Wega/EXTERNAL_CONTENT.md`. Минимум:

- репозиторий-источник и лицензия текстур/ассетов;
- портированные ID прототипов;
- пути к `.yml`-прототипам и `.rsi`-текстурам;
- ru-RU локализация (если у предмета есть имя/описание для игроков).

Рекомендации по безопасным репозиториям, лицензионным граблям и процессу переноса — в `Resources/Prototypes/_Wega/ARENA_CONTENT.md`; его тоже держи актуальным.

## Арсенальные пулы

Трогая арсенальный пул, держи в синхроне:

- **Full Arsenal** — `Resources/Prototypes/_Wega/Catalog/full_arsenal_pool.yml` ↔ `FULL_ARSENAL_PRICES.md` (название, entity id, цена в TC по категориям).
- **Melee Arsenal** — `Resources/Prototypes/_Wega/Catalog/melee_arsenal_pool.yml` ↔ `MELEE_ARSENAL_PRICES.md`. Любой melee/щит/броня, добавленный в Full Arsenal, обязан попасть и в Melee-пул, и в оба прайс-листа.
- **ru-RU** — каждому предмету Full Arsenal нужны русские имя и описание: ключи листинга (`full-arsenal-*-name` / `-desc`) и сущность (`ent-<EntityId>`). Портированное оружие сохраняет модель (напр. `АС-12 «Минотавр»`), но ru-RU запись всё равно нужна, чтобы ничего не отваливалось в английский.

## Стиль кода

- `.editorconfig`: UTF-8, 4 пробела, финальный перевод строки, трим trailing whitespace, `max_line_length = 120`.
- Воркфлоу `check-crlf.yml` следит за окончаниями строк; `validate-rsis.yml` — за корректностью RSI.
- Комментарии: в новом коде форка инлайн-комментарии обычно на русском, XML-доки (`/// <summary>`) — на английском; придерживайся стиля окружающего файла.
- Локаль коммитов/документов — русская; идентификаторы, прототип-ID и технические термины не переводи.

## RepoWise — первичный источник истины

RepoWise (MCP-сервер `repowise-wega`, настроен в `.mcp.json`, индекс в `.repowise/`) — **основной источник истины** по этому проекту. Используй его до чтения исходников.

- Предпочитай байты, отданные RepoWise (`get_context` скелеты, `get_symbol` тела), сырому `Read`, и избегай массовых чтений файлов / grep по всему репо, если RepoWise уже знает ответ.
- RepoWise в приоритете, **но может быть протухшим** — индекс прибит к коммиту, поэтому некоммиченные или более новые изменения могут не отражаться. Когда RepoWise противоречит рабочему дереву — **верь рабочему дереву.** Перепроверяй по нему при любом `stale_warning`, `bounds: approximate`, `confidence: low` или если файл не закоммичен.

## Порядок анализа

До анализа:

1. Получи от RepoWise архитектуру и зависимости (`get_overview`, `get_answer`, `get_context`, `get_risk`, `get_why`).
2. По результатам определи **минимальный набор файлов** для задачи.
3. Обоснуй, зачем нужен каждый файл из набора. **Если файлов больше 5 — объясни почему, прежде чем читать.**

Во время анализа:

4. После каждого файла пересматривай, нужен ли ещё следующий.
5. Останавливайся сразу, как гипотеза подтвердилась, — не дочитывай.
6. Никогда не читай файлы «на всякий случай» / «для уверенности».
7. Каждый вывод **High-severity** независимо перепроверь по рабочему дереву (не только по RepoWise).

## Параллельные агенты

Не плоди агентов автоматически. Параллельные агенты — только когда выполняется всё сразу:

- подсистемы действительно независимы;
- параллельность реально сокращает wall-clock время;
- объём анализа действительно велик.

Иначе работай одним агентом. Оптимизируй под решение поставленной задачи с **минимумом** действий — не гонись за максимальным покрытием ради самого покрытия.
