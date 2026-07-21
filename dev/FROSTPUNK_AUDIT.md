# Аудит фростпанк-арены — план работ

> Сгенерирован многоагентным аудитом 2026-07-21.
> Находок всего: 71; подтверждено адверсариальной проверкой: 58; отсеяно: 13.
> Пункты «Release-сборка» и «precheck.sh» уже исправлены — оставлены для истории.

# План работ по фростпанк-арене

Корень репозитория: `/Users/meguneri/Programming/wega-mega/`

Проверено прямо сейчас в рабочем дереве: оба класса всё ещё без `partial`, литерал `"Cold"` на `ArenaColdSystem.cs:47` на месте, в `git status` висит только правка `SharedArenaColdSystem.cs` (там `partial` уже добавлен, `[Dependency] _speed` убран — правка безопасна, `partial` теперь избыточен, но не мешает).

---

## САМЫЙ ПЕРВЫЙ ШАГ

Починить Release-сборку — три правки в двух файлах, 5 минут:

1. `/Users/meguneri/Programming/wega-mega/Content.Server/_Wega/Arena/Cold/ArenaColdSystem.cs:26` → `public sealed partial class ArenaColdSystem : EntitySystem`
2. Там же строка 47: вынести литерал в поле — `private static readonly ProtoId<DamageTypePrototype> ColdDamageType = "Cold";` и индексировать по нему (RA0033 запрещает литерал в `Index<T>`).
3. `/Users/meguneri/Programming/wega-mega/Content.Client/_Wega/Arena/Cold/ColdExposureSystem.cs:12` → `public sealed partial class ColdExposureSystem : EntitySystem`

Проверка — **именно Release**, Debug эти ошибки не ловит:
```
dotnet build Content.Server/Content.Server.csproj -c Release
dotnet build Content.Client/Content.Client.csproj -c Release
```

---

## 1. Критично — чинить до живого запуска

### 1.1. RA0049 + RA0033: Release-сборка падает (S)
**Что:** см. «первый шаг».
**Почему:** ломается всё, что собирается в Release — `run-server-release.sh`, `run-client-release.sh`, `./dev/package_windows_standalone.sh` (там `CONFIG="Release"`). Проверено эмпирически прогоном сборки: Client даёт 1 ошибку, Server — 2. Это единственные два типа с `[Dependency]` вне `partial` во всём форке, то есть нарушена и своя конвенция (`ArenaStormSystem`, `DuelArenaSystem`, `ArenaLoserMinionSystem` — все `sealed partial`).
**Файлы:** `/Users/meguneri/Programming/wega-mega/Content.Server/_Wega/Arena/Cold/ArenaColdSystem.cs`, `/Users/meguneri/Programming/wega-mega/Content.Client/_Wega/Arena/Cold/ColdExposureSystem.cs`

### 1.2. precheck.sh даёт ложно-зелёный результат (S)
**Что:** `/Users/meguneri/Programming/wega-mega/dev/precheck.sh:29` проверяет сборку через `grep -qE "error (CS|MSB)"` — RA-ошибки анализатора под фильтр не попадают. Строка 40-43 (`run_tests`) считает провалом только строку «Не пройден» в stdout, поэтому ошибка компиляции тест-проекта, нулевой матч фильтра («Ни один тест не соответствует данному фильтру») и падение тест-хоста оставляют шаг зелёным. Чинить: проверять exit code `dotnet build`/`dotnet test`, а не грепать вывод (либо добавить `RA` в regex как минимум).
**Почему:** именно из-за этой дыры дефект 1.1 дожил до аудита при «зелёном» precheck. Пока precheck врёт, любой следующий пункт плана нечем проверить.
**Файлы:** `/Users/meguneri/Programming/wega-mega/dev/precheck.sh`

> Больше в тир 1 ничего не входит честно: механика холода сейчас **полностью спящая** — `ArenaColdZoneComponent` не навешен ни на один прототип, грид или карту (0 совпадений по всему `Resources/`), ни один C#-код не делает ему `AddComp`/`EnsureComp`. Всё остальное ниже — это либо работа по контенту, либо мины, которые сработают в момент включения зоны.

---

## 2. Чтобы карта вообще заработала

### 2.1. Способ включить зону вживую — до появления карты (S)
**Что:** админ-команда `arenacold on|off` по образцу `/Users/meguneri/Programming/wega-mega/Content.Server/_Wega/Administration/Commands/ArenaZoneCommand.cs` (`AdminFlags.Fun`): `EnsureComp`/`RemComp` `ArenaColdZoneComponent` на гриде арены игрока.
**Почему:** сегодня зону невозможно навесить вообще ничем — ни картой, ни кодом, ни командой. У шторма для сравнения есть и прототип-носитель (`duel_arena_tracker.yml:60`), и команда. Без этого первый живой прогон механики совпадёт с релизом карты, а ~370 строк в трёх сборках так и не выполнят ни одной содержательной ветки. Побочно: `AddComp` на уже проинициализированный грид **корректно** поднимает `MapInitEvent` (`EntityManager.Components.cs:427-429`), так что снегопад запустится — дублировать подписку на `ComponentStartup` не нужно.
**Файлы:** новый `/Users/meguneri/Programming/wega-mega/Content.Server/_Wega/Administration/Commands/ArenaColdCommand.cs`

### 2.2. Ловушка тайлов: снегопад рисуется только над `weather: true` (S, но знать до маппинга)
**Что:** мостить арену **только** `FloorSnow` / `FloorSnowDug` / `FloorIce` (наследники `BaseFloorPlanet`, `planet.yml:8` — `weather: true`; `FloorIce` — явный `weather: true`). Если нужен станционный/деконструируемый вид — заводить свои `_Wega`-тайлы с **явным** `weather: true` по шаблону `arena_cyberpunk_floors.yml`.
**Почему:** `FloorAstroSnow` имеет `weather: false # Corvax` (`floors.yml:1839-1845`), у `FloorAstroIce` и `PlatingSnow` флаг не задан вовсе → дефолт `false` (`ContentTileDefinition.cs:125`), а `SharedWeatherSystem.CanWeatherAffect` на этом делает ранний выход. Замостишь «станционным» снегом — буран будет невидим при зелёном линтере. **Важная поправка к находкам: `PlatingSnow` в «безопасный» список попал ошибочно — над ним погода тоже не рисуется.** Механику холода это не затрагивает (урон/замедление считаются зонально), только визуал.
**Файлы:** `/Users/meguneri/Programming/wega-mega/Resources/Prototypes/Tiles/planet.yml`, `/Users/meguneri/Programming/wega-mega/Resources/Prototypes/Tiles/floors.yml`

### 2.3. Сама карта (L)
**Что:** `/Users/meguneri/Programming/wega-mega/Resources/Maps/_Wega/Arena/arena_winter_99.yml` на базе `/Users/meguneri/Programming/wega-mega/Resources/Maps/_Wega/arena_101x101.yml` (302 строки, format 6, tilemap `{0: Space, 1: FloorSteel}` — чистый скелет, покрыт `Arena101x101Test`). На сущность грида: `- type: ArenaColdZone` + `- type: Parallax`. В центр — `FrostpunkGeneratorWega` + трекер; по краям — `DuelArenaSpawnMarker`/`DuelArenaSpawnMarker1` (`duel_arena_spawn.yml:12,27`), `DuelStartBarrier`/`DuelStartTimer`/`DuelStartButton`/`DuelStartSoundEmitter`/`DuelResetButton` (`duel_barriers.yml`, 222 строки), `DuelCleanupController` (`duel_cleanup.yml:7` — **не** в duel_barriers, находка тут ошиблась).
**Параллакс рисовать не надо:** в `Resources/Prototypes/Corvax/Parallax/glacier.yml` уже лежит готовый многослойный `GlacierPlanet` (5 слоёв, PNG на диске, используется в `corvax_glacier.yml:176`). Кастомный — только если не устроит визуал.
**Почему:** без карты весь холодовой код мёртв, а зелёные сборка/SandboxTest/YAMLLinter о его корректности не говорят ничего.
**Файлы:** новая карта; `/Users/meguneri/Programming/wega-mega/Resources/Prototypes/_Wega/Entities/Markers/Spawners/Conditional/duel_rotation.yml:22-25` (дописать путь), `/Users/meguneri/Programming/wega-mega/Content.IntegrationTests/Tests/_Wega/ArenaMapsLoadTest.cs:15-20` (дописать в массив — иначе карта поедет в прод без проверки загрузки; тест ловит падение из-за `noRot` + ненулевой поворот, а у генератора `noRot: true`).

### 2.4. Тёплые базы — обязательное правило вёрстки (S)
**Что:** в каждую базу поставить `HeatSource` — `Fireplace` (radius 4) или `BurningBarrelWega` (radius 5). Либо разместить базы внутри радиуса генератора.
**Почему:** `ArenaColdSystem.Update` не смотрит ни на `DuelArenaComponent.IsActive`, ни на фазу; зона резолвится по `GridUid`/`MapUid`, а базы у дуэльных карт лежат **на том же гриде**, что и площадка (проверено на `DMarenaWALLrotation.yml`: ровно один `MapGrid`). Дуэлянт у кнопки готовности ждёт соперника сколько угодно, а при дефолтах замедление начнётся через 2.7 с, урон — через 13.6 с. Плюс `OnMobStateChanged` (`DuelArenaSystem.cs:542-556`) реагирует на крит только при `IsActive && Duelists.Contains(uid)` — то есть замёрзший в базе между раундами не подхватывается вообще и добивается насмерть в запечатанной базе.
**Файлы:** карта; правило — в `/Users/meguneri/Programming/wega-mega/ARENA_MODE.md`

### 2.5. Снятие `ColdExposureComponent` при смерти и Rejuvenate (S)
**Что:** в `ArenaColdSystem.Initialize` подписаться на `RejuvenateEvent` и `MobStateChangedEvent(Dead)` для `ColdExposureComponent` → `RemComp` + `RefreshMovementSpeedModifiers`. Либо убрать `continue` на строке 77 в пользу общей ветки оттаивания для мёртвых.
**Почему:** сейчас мёртвый моб пропускается **до** ветки, которая единственная снимает компонент (`ArenaColdSystem.cs:77-78` против 82-95). Труп уносит застывший networked `Level`, у игрока залипает полноэкранный иней до воскрешения, а `PurgeDuelistTraces` (`DuelArenaSystem.cs:713-760`) — специально задуманное место для «следов боя» — про холод не знает.
**Честно про масштаб:** последствия скромные. После `PerformRejuvenate` боец жив и оттаивает за ~3 с (`OffZoneWarmPerSecond 0.35`), а замедление перестаёт применяться ниже порога 0.15 за ~2.4 с; между раундами есть `RestoreDelay 2.5 c`, `ReturnGrace 20 c` и ручной старт по кнопке. Утечки нет — мёртвому компонент никогда не добавляется. Это косметика + гигиена, но фикс на 5 строк.
**Файлы:** `/Users/meguneri/Programming/wega-mega/Content.Server/_Wega/Arena/Cold/ArenaColdSystem.cs`

### 2.6. Пресет шторма под 99×99 и разнесение центров (S)
**Что:** (а) отдельный прототип трекера для зимней карты: `initialRadius` под полудиагональ (~72), `shrinkStep`/`shrinkInterval` пересчитать; (б) поставить трекер шторма **со смещением 15-20 тайлов** от генератора; (в) тип урона шторма `Heat: 5` → `Cold`.
**Почему:** нынешний `DuelArenaTrackerStorm` (`initialRadius 28`) подобран под `arena_duel_31` и на 99×99 через 30 с оставит играбельными 25% площади. При соосных центрах шторм с 54-й секунды полностью поглощает тёплый диск (r=20): обе механики толкают в одну точку и гасят друг друга, холод перестаёт что-либо решать. Разнесённые центры дают настоящий выбор «тепло или безопасно» — и это правка **только карты**.
**Поправка:** утверждение «боец из угла физически не успеет добежать» неверно — 30 с `startDelay` × 4.5 тайла/с = 135 тайлов при нужных 42. Аргумент только в схлопывании играбельной зоны.
**Файлы:** `/Users/meguneri/Programming/wega-mega/Resources/Prototypes/_Wega/Entities/Markers/Spawners/Conditional/duel_arena_tracker.yml`

---

## 3. Улучшения качества игры

### 3.1. Тюнинг чисел холода — только после первого живого прогона (M)
**Что:** развязать радиус тепла и скорость промерзания. Стартовые ориентиры из аудита: радиус генератора 20 → 10-12, `ColdPerSecond` 0.055 → 0.10-0.12, `WarmPerSecond` 0.3 → 0.18-0.20. Плюс 4-6 бочек `BurningBarrelWega` по среднему кольцу как «тёплый архипелаг».
**Почему:** при текущих числах на 99×99 порог урона наступает через 13.6 с, за это время боец пробегает ~55 тайлов — то есть безнаказанно достижима почти вся карта, а 1 с у домны оплачивает 5.45 с мороза (равновесный «дежурный цикл» — 15.5% времени). Механика почти не создаёт решений.
**Честно:** все эти цифры — расчётные, ни одна не проверена прогоном; часть расчётов в аудите игнорировала само замедление и потому занижала эффект. Одно решение механика создаёт уже сейчас: возвращаться с −35% скорости в дуэли 1на1 больно. **Не крутить числа до плейтеста.**
**Файлы:** `/Users/meguneri/Programming/wega-mega/Content.Shared/_Wega/Arena/Cold/ArenaColdZoneComponent.cs`, `/Users/meguneri/Programming/wega-mega/Resources/Prototypes/_Wega/Entities/Structures/Specific/frostpunk_generator.yml`

### 3.2. Вынести пороги и урон в `ArenaColdZoneComponent` (S)
**Что:** перенести `EffectThreshold`, `MinSpeedMultiplier`, `ProtectedMinSpeedMultiplier`, `DamageThreshold`, а также `DamagePerTick` (как `DamageSpecifier`-DataField), `ProtectedDamageMultiplier`, `OffZoneWarmPerSecond` и `Interval` в компонент зоны; копировать их на моба при `EnsureComp`.
**Почему:** сейчас всё, кроме `ColdPerSecond`/`WarmPerSecond`/`Weather`, недостижимо из YAML: `ColdExposureComponent` вешается рантаймом через `EnsureComp`, который читает C#-дефолты, а не прототип. Обходной путь «объявить компонент на прототипе моба» тоже не работает — при полном отогреве вне зоны идёт `RemComp`, и следующий `EnsureComp` пересоздаёт с дефолтами. Урон и множитель 0.5 захардкожены в `Initialize`. Без этого 3.1 требует пересборки сервера на каждую цифру.
**Файлы:** `/Users/meguneri/Programming/wega-mega/Content.Shared/_Wega/Arena/Cold/ArenaColdZoneComponent.cs`, `ColdExposureComponent.cs`, `ArenaColdSystem.cs`

### 3.3. Собственный признак утепления вместо `TemperatureProtection` (S)
**Что:** завести `_Wega`-компонент/тег `ColdInsulation` и проверять его в `HasColdProtection` (`ArenaColdSystem.cs:169-178`) вместо `HasComp<TemperatureProtectionComponent>`.
**Почему:** сейчас «зимней курткой» считается любая верхняя одежда с этим компонентом — все ~38 хардсьютов, MOD-костюмы, EVA, фаер-костюмы, `ClothingOuterVestWebElite`, латунная кираса, костюм исследователя. Разница ощутимая: урон вдвое меньше и замедление 0.85 вместо 0.65. XML-комментарий обещает «A winter coat is the one thing that helps» — комментарий врёт.
**Две важные поправки к находкам, которые надо знать до правки:**
- **Утверждение «зимней куртки в арсенале нет» — ложное.** В `full_arsenal_pool.yml` есть минимум 5 зимних курток (`FullArsenalWinterHoS` 4 TC, `FullArsenalWinterPilot` 3 TC, `WinterSyndieCapArmored`, `WinterBlueShield`, `WinterBlueShieldAlt`), они же зеркально в melee-пуле. Плюс `FullArsenalCoatTrench` за **1 TC** с тем же `coolingCoefficient: 0.1`. Дешёвая утеплительная позиция уже есть — добавлять ничего не нужно.
- **Предложенный порог `CoolingCoefficient <= 0.2f` не работает:** у зимней куртки 0.1, у элитного веста тоже ровно 0.1, у хардсьютов 0.001-0.05. По холодовому коэффициенту куртка и вест неразличимы, а хардсьют «изолирует лучше». Единственный жизнеспособный вариант — свой компонент.
- Тематически прокси корректен для курток, полярного костюма и хардсьютов; реально паразитируют только элитный вест и обычный костюм исследователя.
**Файлы:** `/Users/meguneri/Programming/wega-mega/Content.Server/_Wega/Arena/Cold/ArenaColdSystem.cs`, новый компонент в `Content.Shared/_Wega/Arena/Cold/`

### 3.4. Не добивать лежачего в крите (S)
**Что:** в `ArenaColdSystem.cs:77` заменить `mob.CurrentState == MobState.Dead` на пропуск любого incapacitated (`_mobState.IsIncapacitated`).
**Почему:** сейчас холод доводит критуемого до смерти. Для активной дуэли это безобидно (крит мгновенно завершает бой через `ConcludeDuel` + `PerformRejuvenate` в том же стеке события), но для крита **вне** активного боя и для NPC — нет. Дешёвая страховка на любую конфигурацию карты.
**Файлы:** `/Users/meguneri/Programming/wega-mega/Content.Server/_Wega/Arena/Cold/ArenaColdSystem.cs`

### 3.5. Решить судьбу NPC (S)
**Что:** либо фильтровать цикл по игрокам (`ActorComponent`/`MindContainer`), либо ввести `ColdImmuneComponent` и вешать на боссов (`MobBossArena`, `MobBossGoliath`, `MobBossDancer`), `MobSyndicateArrester` и миньонов; плюс пропускать мобов в контейнерах (`_container.IsEntityInContainer`).
**Почему:** `Update` перебирает всех живых мобов без фильтра, урон идёт с `ignoreResistances: true` — босс получает 1/с мимо брони и резистов, ~285 за пятиминутный бой, что не заложено в `BossArenaSystem`. Плюс замедление до 0.65. Если холод для NPC оставляют осознанно — записать это в `ARENA_MODE.md` и пересчитать их HP.
**Поправки:** «миньонов проигравшего» как NPC не существует — `ArenaLoserMinionSystem.SpawnMinion` только вешает компонент на самого игрока. Боссы и «арест-бот» — не роботы, а `MobHuman`, инвентарь у них есть, так что вариант «повесить им `TemperatureProtection` на экипировку» тоже рабочий и дешёвый. Силиконы/борги урон не получат в любом случае (`Cold` не входит в их `DamageContainer`), но замедляться и видеть иней будут.
**Файлы:** `/Users/meguneri/Programming/wega-mega/Content.Server/_Wega/Arena/Cold/ArenaColdSystem.cs` + прототипы боссов

### 3.6. Тепло как ресурс: затухание генератора вместо разрушения (M)
**Что:** (а) добавить в `HeatSourceComponent` понятие «текущий радиус / включён» — сейчас там одно поле `Radius` без флага; (б) плавно снижать радиус генератора по ходу раунда синхронно со штормом, финальная стадия — переключение спрайта на уже нарисованный стейт `off` через `Appearance`; (в) `Damageable` + `Injurable` на `BurningBarrelWega`, чтобы её `Destructible` заработал.
**Почему:** генератор наследует `BaseStructure`, у которого нет ни `Damageable`, ни `Destructible` — его нельзя ни сломать, ни выключить, стейт `off` в RSI мёртвый. **Ключевая поправка:** `BurningBarrelWega` тоже неразрушима — у неё есть `Destructible` (порог 50), но нет `Damageable`, а `DestructibleSystem` подписан на `DamageChangedEvent`, который поднимает только `DamageableSystem`. То есть комментарий в `arena_decoration_metro.yml:20` («островок тепла, который можно расстрелять») сейчас **врёт**, и заявленного контраста «неуязвимый генератор vs хрупкая бочка» не существует — неуязвимо и то, и другое. Побочно тот же корень даёт «потушенный костёр продолжает греть» у апстримных `Bonfire`/`Fireplace`.
**Дизайн-рекомендация:** центральный генератор разрушаемым **не** делать — в 1на1 тот, кто первым сломает домну, конвертирует дуэль в подкидывание монетки. Тактический разрушаемый слой — на бочки.
**Файлы:** `/Users/meguneri/Programming/wega-mega/Content.Shared/_Wega/Arena/Cold/HeatSourceComponent.cs`, `/Users/meguneri/Programming/wega-mega/Resources/Prototypes/_Wega/Entities/Structures/Specific/frostpunk_generator.yml`, `/Users/meguneri/Programming/wega-mega/Resources/Prototypes/_Wega/Entities/Structures/Decoration/arena_decoration_metro.yml`

### 3.7. Интеграционный тест на холод (M)
**Что:** `/Users/meguneri/Programming/wega-mega/Content.IntegrationTests/Tests/_Wega/Arena/Cold/ArenaColdSystemTest.cs`, namespace **обязательно** с сегментом `_Wega` (`Content.IntegrationTests.Tests._Wega.Arena.Cold`) — иначе фильтр precheck его не подхватит. Кейсы: накопление `Level` и оттаивание у `HeatSource`; `RemComp` при выходе из зоны; урон после `DamageThreshold`; обход резистов (дамми с `damageModifierSet` Cold: 0 всё равно получает урон); защита только из слота OUTERCLOTHING и вдвое меньший урон; компонент снят после смерти/Rejuvenate.
**Практические грабли:** зону вешать самому (`AddComp(grid, new ArenaColdZoneComponent { Weather = null, ColdPerSecond = 0.5f, ... })` — конструировать компонент **до** `AddComp`, иначе `MapInitEvent` успеет дёрнуть `TrySetWeather`); дамми нужен `InjurableComponent`, иначе урон не осядет; для «тёплого» моба ассертить `Level == 0`, а не отсутствие компонента (`EnsureComp` вызывается и внутри тепла); `GetTotalDamage` помечен `[Obsolete]`.
**Почему:** сейчас у системы ноль тестов, а конвенция форка — тест на каждую систему (`DuelArenaSystemTest`, `ArenaAirstrikeSystemTest`, `RaidControllerTest` и т.д.). `RunSeconds` — прокрутка тиков, не реальное время (соседний тест спокойно делает `RunSeconds(240f)`), так что «14 секунд на кейс» ничего не стоят.
**Файлы:** новый тест

### 3.8. Реконн шторма в буран (S)
**Что:** вынести цвета кольца из `ArenaStormOverlay.cs` (строки 77, 86, 90, 96 — насыщенно-красный/оранжевый) в `[DataField]` на `ArenaStormComponent` и задать холодную палитру на зимней карте; `StormSound` → `/Audio/Effects/Weather/snowstorm.ogg` (файл в репо); на старте сжатия переключать погоду `WeatherSnowfallMedium` → `WeatherSnowfallHeavy` (оба готовы); анонс в чат `ArenaStormSystem.cs:148` идёт `Color.OrangeRed`.
**Почему:** шторм не спавнит ни одной сущности и не грузит ни одной текстуры — вся визуализация векторная, поэтому реконн это «два-три числа и путь к файлу», ассетов не требует. `[AutoNetworkedField]` цветам не нужен: клиент создаёт сущность из того же прототипа (тот же приём уже используется для `ShrinkStep`/`MinRadius`).
**Файлы:** `/Users/meguneri/Programming/wega-mega/Content.Client/_Wega/Duel/ArenaStormOverlay.cs`, `/Users/meguneri/Programming/wega-mega/Content.Shared/_Wega/Duel/ArenaStormComponent.cs`

### 3.9. Алерт холода в HUD (S)
**Что:** свой alert-прототип в `Resources/Prototypes/_Wega/Alerts/` с апстримными иконками `/Textures/Interface/Alerts/temperature.rsi` (cold1/cold2/cold3) и своими ftl-ключами ru+en; severity 1..3 по `Level` (~0.15 / 0.45 / 0.75).
**Почему:** в системе нет ни одного `Loc.`/алерта/попапа — единственная обратная связь это иней и градуированное замедление, числовой шкалы нет. Апстримный `id: Cold` (`alerts.yml`) переиспользовать **не стоит**: его en-US описание советует «take off any insulating clothing like a space suit», что для арены прямо вводит в заблуждение (ru-RU текст, наоборот, советует одеться).
**Честно:** «понятно ли, помогает ли куртка» это не решает — `Protected` на `Level` не влияет, нужен отдельный признак/иконка.
**Файлы:** новые прототип + локали

### 3.10. Перф-гигиена тика (S)
**Что:** ранний выход в начале `Update`, если активных зон нет (держать `HashSet<EntityUid>` по `ComponentStartup`/`ComponentShutdown`); отсекать мобов по `HashSet<MapId>` холодных карт до `TryComp`; переиспользовать словарь источников тепла (`Clear` вместо `new`); не дёргать `RefreshMovementSpeedModifiers`, пока `Math.Max(Level, beforeLevel) <= EffectThreshold` (там множитель заведомо 1.0).
**Почему:** `Update` раз в секунду безусловно аллоцирует `Dictionary`+`List` и проходит **всех** мобов сервера, при том что зон нет вообще — 100% холостая работа на каждом раунде любого режима.
**Честно:** абсолютная цена ничтожна (десятки микросекунд раз в секунду), это гигиена, а не просадка. Не делать кэш источников тепла с инвалидацией — `Bonfire` строится игроками через `Construction`, риск фантомных тёплых зон дороже выигрыша. Идея «квантовать `Level` шагом 0.05» не работает: шаг 0.055 больше, условие срабатывает каждый тик.
**Файлы:** `/Users/meguneri/Programming/wega-mega/Content.Server/_Wega/Arena/Cold/ArenaColdSystem.cs`

---

## 4. Приятные мелочи

| # | Что | Почему | Усилия | Файлы |
|---|-----|--------|--------|-------|
| 4.1 | Сбрасывать `_intensity = 0f` при `AddOverlay` (публичный `Reset()`), + снимать `ColdExposureComponent` в ветке «в зоне, но тепло» при `Level == 0` | Оверлей — один долгоживущий инстанс, `FrameUpdate` идёт только пока он зарегистрирован. Умер замёрзшим → ушёл в гост при `_intensity ~0.7` → следующее тело видит белую вспышку на полсекунды-секунду. Тот же паттерн уже есть у Sandevistan-оверлеев, то есть это идиома форка, а не регрессия | S | `/Users/meguneri/Programming/wega-mega/Content.Client/_Wega/Arena/Cold/ColdExposureOverlay.cs`, `ColdExposureSystem.cs` |
| 4.2 | `MaxAlpha` 0.85 → 0.55-0.65 | Иней остаётся сигналом состояния, но перестаёт быть дебаффом зрения. **Переделывать текстуру не нужно:** замеры альфы показали, что плотность уже сосредоточена в ободке 40-80 px (~0.8 тайла), за 150 px кадр практически чистый — то есть предложенное «перенести плотность в узкую рамку» описывает текущее состояние | S | `ColdExposureOverlay.cs:33` |
| 4.3 | Проверить оверлей на 21:9 | `DrawTextureRect` натягивает фиксированные 1024×576 на любой вьюпорт: на ultrawide кайма получает разную толщину по X/Y, снежинки становятся эллипсами. Чинить 9-slice/тайлингом только если реально заметно | S | `ColdExposureOverlay.cs`, `Tools/gen_frost_overlay.py` |
| 4.4 | `ComponentShutdown` для зоны → `TrySetWeather(mapId, null, out _)` (MapID брать до открепления transform) | Снегопад ставится бессрочно и не снимается никогда при `RemComp`/удалении грида. Косметика, в штатном сценарии карта удаляется целиком | S | `ArenaColdSystem.cs` |
| 4.5 | Поправить докстринг `SharedArenaColdSystem` | «Runs on both sides so the slowdown is predicted» — неправда: замедление считается на сервере и реплицируется через `[AutoNetworkedField]`. Реальная роль shared-обработчика — чтобы чужие клиентские пересчёты не затирали холодовой множитель. Настоящее предсказание в лоб не сделать: `RefreshMovementSpeedModifiers` имеет ранний выход при `_timing.ApplyingState` | S | `/Users/meguneri/Programming/wega-mega/Content.Shared/_Wega/Arena/Cold/SharedArenaColdSystem.cs` |
| 4.6 | Собрать пострадавших в список и наносить урон после `mobs.Dispose()` | `TryChangeDamage` вызывается внутри живого `EntityQueryEnumerator` по словарю `MobStateComponent`; смерть синхронно уводит в `ConcludeDuel`. Удаление из Dictionary при перечислении безопасно, **добавление** — исключение. Сегодня спавна мобов в каскаде нет, но привязка хрупкая. Это идиома апстрима (`BarotraumaSystem`, `RottingSystem` делают так же), поэтому профилактика, а не баг | S | `ArenaColdSystem.cs` |
| 4.7 | `dev/FROSTPUNK_ARENA.md` — реестр готового зимнего контента | Тайлы, стены, флора, 29 декалей, погода, звук, источники тепла, карты-доноры — всё инвентаризовано и проверено, рисовать почти ничего не нужно. Записать, чтобы не изобретать при маппинге. Три поправки к реестру: прототипа `FloraTreeSnow01..06` **не существует** (только `FloraTreeSnow` с `RandomSprite`, старые id живы через `migration.yml`); `WeatherHail` — в другом месте `weather.yml`; снежные мобы лежат в `icemoon_megafauna.yml`/`icemoon_fauna.yml`, а `_Wega/Actions/icemoon.yml` — их экшены | S | новый файл в `dev/` |
| 4.8 | `ArenaMapsLoadTest` читает список арен из прототипа `DuelRotationController`, а не хардкодит | Сейчас любая новая арена ротации поедет без покрытия молча — тест не покраснеет | S | `/Users/meguneri/Programming/wega-mega/Content.IntegrationTests/Tests/_Wega/ArenaMapsLoadTest.cs` |
| 4.9 | Зимние пропы/стены/декали — **после** карты, не до | Своих фростпанк-пропов кроме генератора нет, зимних арена-стен нет, напольных снежных декалей (сугробы, наледь, позёмка) нет. Но: 17 киберпанк-стен форка не использует **ни одна** карта репо — генерировать ещё 6-8 неиспользуемых прототипов до карты значит повторить тот же паттерн. Для первого играбельного прохода хватает `WallIce`, `WallSnowCobblebrick`, `WallRockSnowIndestructible` (собственный прототип форка, `_Wega/.../asteroid.yml:308`) + `FloorSnow`/`FloorIce`/`FloorSnowDug` + существующих декалей. `permafrost.png` лежит в репо неиспользованной — бесплатная текстура под тайл «вечная мерзлота» | M | `Tools/gen_arena_winter_pack.py` (новый) |

---

## Противоречия и недоразумения в исходных находках

Стоит знать, чтобы не чинить то, что не сломано:

1. **«Зимней куртки в арсенале нет»** — ложь, повторённая в двух находках. Их пять плюс тренчкот за 1 TC. Вывод «трейд-офф перевёрнут» не следует.
2. **Предложенный фикс утепления через `CoolingCoefficient <= 0.2f`** не отсекает ни один спорный предмет — куртка и элитный вест оба 0.1. Только свой компонент.
3. **«BurningBarrelWega можно расстрелять»** — нет: `Destructible` без `Damageable` мёртв. Одна находка строила на этом контраст с генератором; контраста нет, неуязвимо и то и другое, а комментарий в yml вводит в заблуждение.
4. **«Debug/precheck молчат, потому что собирают в Debug»** — механизм другой: `precheck.sh` собирает как раз в Release, но грепает только `CS|MSB`. Это отдельная дыра (п. 1.2).
5. **«AddComp зоны в рантайме не запустит снегопад»** — неверно, движок сам поднимает `MapInitEvent` на map-инициализированной сущности. Дублировать подписку на `ComponentStartup` не нужно и вредно.
6. **`winterreserve.yml` как донор зимнего ландшафта** — ложный след: это интерьер жилого модуля, в его tilemap только `FloorAstroSnow`/`FloorAstroIce`, оба без погоды. Донор ландшафта — `snowy_labs.yml`. Но и оттуда копировать целиком нельзя: `PlatingSnow` и `FloorAstroIce` там тоже без погоды.
7. **«ColdExposure переживает раунд и никогда не рассосётся»** — переоценено: после `Rejuvenate` боец жив и оттаивает за ~3 с, ранее найденной «утечки на трупах» нет.
8. **«Боец из угла не успеет добежать от шторма»** — арифметическая ошибка, `startDelay` 30 с даёт трёхкратный запас.
9. **«Зимнего параллакса нет, нужен генератор»** — есть готовый `GlacierPlanet` в `Corvax/Parallax/glacier.yml`. Заодно: собственный `WegaArenaUrban` и `WegaUrbanBG.png` написаны и **нигде не подключены** — мёртвый груз.
10. **«Контент не закоммичен»** (из постановки задачи) — устарело: весь фростпанк-слой уже в коммите `0bebf78446 "card + cold"`, в рабочем дереве изменён только `SharedArenaColdSystem.cs`. Планировать надо от истории, риска затереть несохранённое нет.

**Общий контекст честности:** пункты 3 и 4 (и почти весь тир 2) описывают поведение, которое **ни разу не выполнялось** — зона не навешана нигде, карты нет, тестов нет. Все балансные числа расчётные. Поэтому логика плана такая: сначала починить сборку и precheck (тир 1), затем дать себе способ включить механику вживую (2.1) и построить карту (2.2-2.4), и только после первого реального прогона трогать баланс (3.1).
