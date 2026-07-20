# `_Wega` — кастомный контент форка

Эта папка содержит прототипы, текстуры, звуки, локализации и другие ресурсы, специфичные для форка `wega-mega`. Контент организован так, чтобы не пересекаться с upstream: прототипы лежат в `Entities/`, текстуры — в `Textures/_Wega/`, локализация — в `Resources/Locale/ru-RU/...`.

## Перенесённый контент для арены / тарков-режима

Подробный гайд по источникам, лицензиям и процессу переноса — в [`ARENA_CONTENT.md`](ARENA_CONTENT.md). Здесь краткая сводка всего, что уже портировано из внешних репозиториев.

### Репозитории-источники

| Репозиторий | Основные лицензии | Что перенесено |
|---|---|---|
| [Delta-V](https://github.com/DeltaV-Station/Delta-v) | `CC0-1.0`, `CC-BY-SA-3.0`, `CC-BY-SA-4.0` | Броня, жилеты, куртка, униформа, головные уборы, оружие ближнего боя |
| [Frontier Station / Nyanotrasen](https://github.com/new-frontiers-14/frontier-station-14) | `CC-BY-SA-3.0`, `CC-BY-SA-4.0` | Гладиаторская броня, богу, самурайские доспехи, шлемы кендо/кабуто, reflective vest, бронированный тренч охотника за головами, униформа почтальона/тюремщика/футболки с джинсами, одеяние епископа, плащ охотника на ведьм, мантии волшебника |
| [Einstein Engines](https://github.com/Simple-Station/Einstein-Engines) | `CC-BY-SA-3.0` | Костюм SWAT |
| [Metro 14](https://github.com/Metro-Observers/metro-14-official) | `CC-BY-SA-3.0` | Горящая бочка-костёр, ржавые/цветные бочки-хранилища, радиоактивная и топливная бочки |

### Пометки в игре

Чтобы не путаться, у всех перенесённых предметов в игровом `name` указан источник в скобках: `(Delta-V)`, `(Nyanotrasen)`, `(Frontier Station)`, `(Einstein Engines)` или `(Metro 14)`. То же самое продублировано в `ru-RU` локализации.

### Список прототипов

#### Декор / постройки

- `BurningBarrelWega` — бочка-костёр (Metro 14)
- `BarrelRustedWega` — ржавая бочка-хранилище (Metro 14)
- `BarrelBlueWega` — синяя бочка-хранилище (Metro 14)
- `BarrelYellowWega` — жёлтая бочка-хранилище (Metro 14)
- `BarrelRadiationWasteWega` — бочка с радиоактивными отходами (Metro 14)
- `BarrelFuelWega` — бочка со сварочным топливом (Metro 14)
- `BarrelToxinWega` — бочка с токсинами (Metro 14)

#### Головные уборы / шлемы

- `ClothingHeadHelmetKendoMenWega` — мен (Nyanotrasen)
- `ClothingHeadHelmetKabutoWega` — кабуто и менпо (Nyanotrasen)
- `ClothingHeadHatCapSecWega` — кепка СБ (Delta-V)
- `ClothingHeadHatBeretDetWega` — берет детектива (Delta-V)

#### Броня / жилеты

- `ClothingOuterArmorPlateCarrierWega` — разгрузочный жилет (Delta-V)
- `ClothingOuterArmorDuraVestWega` — дюратканевый жилет (Delta-V)
- `ClothingOuterArmorARCWega` — КСБ / ARCS (Delta-V)
- `ClothingOuterVestFlakWega` — противоосколочный жилет (Delta-V)
- `ClothingOuterVestPlateCarrierAdvWega` — улучшенный разгрузочный жилет (Delta-V)
- `ClothingOuterChameleonArmorWega` — хамелеон-бронежилет (Delta-V)
- `ClothingOuterArmorGladiatorWega` — гладиаторская броня (Nyanotrasen)
- `ClothingOuterArmorKendoBoguWega` — богу (Nyanotrasen)
- `ClothingOuterArmorTouseiGusokuWega` — тоусэй-гусоку (Nyanotrasen)
- `NFClothingOuterArmorReflectiveWega` — базовый светоотражающий жилет (Frontier Station)
- `ClothingOuterArmorSwatWega` — костюм SWAT (Einstein Engines)

#### Верхняя одежда

- `ClothingOuterCoatCybersunWindbreakerWega` — ветровка Cybersun (Delta-V)
- `ClothingOuterCoatBHTrenchWega` — бронированный тренч охотника за головами (Frontier Station)
- `ClothingOuterCoatBishopWega` — одеяние епископа (Frontier Station)
- `ClothingOuterCoatWitchHunterWega` — плащ охотника на ведьм (Frontier Station)
- `ClothingOuterWizardBrickWega` — кирпичная мантия волшебника (Frontier Station)
- `ClothingOuterFakeWizardWega` — фальшивая мантия волшебника (Frontier Station)

#### Униформа

- `ClothingUniformBlackTurtleneckWega` — чёрная водолазка (Delta-V)
- `ClothingUniformJumpsuitSecTurtleWega` — водолазка старшего офицера (Delta-V)
- `ClothingUniformJumpsuitSecWhiteWega` — белый комбинезон СБ (Delta-V)
- `ClothingUniformJumpsuitMailCarrierWega` — комбинезон почтальона (Nyanotrasen)
- `ClothingUniformJumpskirtMailCarrierWega` — юбка-комбинезон почтальона (Nyanotrasen)
- `ClothingUniformJumpsuitTshirtJeansWega` — белая футболка и джинсы (Nyanotrasen)
- `ClothingUniformJumpsuitTshirtJeansGrayWega` — серая футболка и джинсы (Nyanotrasen)
- `ClothingUniformJumpsuitTshirtJeansPeachWega` — персиковая футболка и джинсы (Nyanotrasen)
- `ClothingUniformJumpsuitPrisonGuardWega` — форма тюремщика (Nyanotrasen)

#### Оружие ближнего боя

- `AdvancedTruncheonWega` — улучшенная дубинка (Delta-V)
- `SilverSwordWega` — серебряный меч (Delta-V)

### Где лежат файлы

| Что | Путь к прототипам | Путь к текстурам |
|---|---|---|
| Вся броня | `Entities/Clothing/OuterClothing/arena_armor*.yml` | `Textures/_Wega/Clothing/OuterClothing/Armor/` |
| Куртки | `Entities/Clothing/OuterClothing/arena_coats_dv.yml`, `arena_coats_frontier.yml` | `Textures/_Wega/Clothing/OuterClothing/Coats/` |
| Униформа | `Entities/Clothing/Uniforms/arena_uniforms_dv.yml`, `arena_uniforms_ny.yml` | `Textures/_Wega/Clothing/Uniforms/Jumpsuit/`, `Jumpskirt/` |
| Головные уборы | `Entities/Clothing/Head/arena_hats_dv.yml`, `arena_hats_ny.yml` | `Textures/_Wega/Clothing/Head/Hats/` |
| Оружие ближнего боя | `Entities/Objects/Weapons/Melee/arena_melee_dv.yml` | `Textures/_Wega/Objects/Weapons/Melee/` |
| Декор / бочки | `Entities/Structures/Decoration/arena_decoration_metro.yml` | `Textures/_Wega/Structures/Decoration/` |
| Локализация | — | `Resources/Locale/ru-RU/ss14-ru/prototypes/entities/...` |

### Проверка

После добавления новых прототипов запускать:

```bash
dotnet run --project Content.YAMLLinter --no-build
```

Все текущие переносы проходят линтер без ошибок.

## Карточные игры (Estação Pirata / CorvaxGoob)

Источник: [space-syndicate/Goob-Station](https://github.com/space-syndicate/Goob-Station) (CorvaxGoob).
Код — **AGPL-3.0-or-later** (SPDX-заголовки сохранены в файлах), текстуры/звуки — атрибуция в
`attributions.yml` рядом с ассетами. Первоисточник системы карт — бразильский форк Estação Pirata.
⚠️ AGPL: игрокам сервера по запросу нужно предоставлять исходники этой части.

Перенесено с сохранением оригинальных префиксных каталогов:

- **Код**: `Content.{Shared,Client}/_EstacaoPirata/Cards/` (карты/колоды/руки/стопки + UI руки),
  `Content.Server/_EstacaoPirata/OpenTriggeredStorageFill/`. Адаптация под форк: системы сделаны
  `partial`, `[Dependency]`-поля без `readonly` (RA0049/RA0051).
- **Прототипы**: `Resources/Prototypes/EstacaoPirata/Entities/Objects/Misc/{black,nt,syndicate}_cards.yml`
  (обычные игральные колоды), `EstacaoPirata/SoundCollections/cards.yml`,
  `Resources/Prototypes/_CorvaxGoob/Entities/Objects/Misc/kotahi_cards.yml` — **Kotahi** (аналог UNO):
  колода `CardBoxKotahi`, книга правил `BookKotahiRules`.
- **Ассеты**: `Resources/Textures/EstacaoPirata/Objects/Misc/cards.rsi`,
  `Resources/Textures/_CorvaxGoob/Objects/Fun/Tabletop/Kotahi/rulebook.rsi`,
  `Resources/Audio/EstacaoPirata/Effects/Cards/`.
- **Локализация**: имена всех 230 сущностей разрешаются и в ru-RU, и в en-US (частью через ключи
  `ent-*`, частью через loc-ключи в поле `name:` — так сделано в исходнике для номиналов карт).
  Правила Kotahi: `Locale/{ru-RU,en-US}/_CorvaxGoob/entities/objects/fun/tabletop/kotahi/rulebook.ftl`.
  Русский текст правил переписан (в исходнике — сломанная разметка `[bold]`, ошибки и обрывы фраз),
  английской версии в исходнике не было вовсе — написана с нуля. Правила выверены по фактическому
  составу колоды из `kotahi_cards.yml`: 108 карт, 4 цвета × 25 + 8 чёрных.

Отличия от источника: вырезан Goob-only компонент `ThrowableBlocked` и хим-гиммики метательных
карт (`SolutionContainerManager`/`SolutionRegeneration`/`SolutionInjectWhileEmbedded` — несовместимые
поля и отсутствующий у нас реагент Tirizene); карты остались метательными, но без инъекций.
Uplink-каталог Estação Pirata не переносился.

## Чего избегать

- **Nuclear-14** — много крутого постапокалиптического контента, но текстуры под `CC-BY-NC-SA-3.0` (non-commercial), поэтому не подходят.
- Репозитории без чёткой лицензии или с портами из коммерческих игр.
