# Контент для арены из внешних репозиториев SS14

Этот документ собирает проверенные источники и рекомендации по портированию вещей для арены/тарков-режима в `_Wega`. Контент должен быть совместим с лицензией проекта — предпочтительно `CC0-1.0` или `CC-BY-SA-3.0`. `CC-BY-NC-SA` и прочие non-commercial лицензии **не подходят**.

## Уже перенесённый контент

| Репозиторий | Лицензия текстур | Что перенесено | Куда в `_Wega` |
|---|---|---|---|
| [Delta-V](https://github.com/DeltaV-Station/Delta-v) | `CC0-1.0` | Plate carrier, durathread vest, ARCS (riot suit) | `Entities/Clothing/OuterClothing/arena_armor.yml`, `Textures/_Wega/Clothing/OuterClothing/Armor/{platecarrier,duravest,riot}.rsi` |
| [Delta-V](https://github.com/DeltaV-Station/Delta-v) | `CC0-1.0` | Flak jacket, advanced plate carrier | `Entities/Clothing/OuterClothing/arena_armor.yml`, `Textures/_Wega/Clothing/OuterClothing/Vests/{flak,advcarrier}.rsi` |
| [Delta-V](https://github.com/DeltaV-Station/Delta-v) | `CC-BY-SA-3.0` | Advanced truncheon, silver sword | `Entities/Objects/Weapons/Melee/arena_melee_dv.yml`, `Textures/_Wega/Objects/Weapons/Melee/{advanced_truncheon,silversword}.rsi` |
| [Delta-V](https://github.com/DeltaV-Station/Delta-v) | `CC-BY-SA-3.0` | Cybersun windbreaker | `Entities/Clothing/OuterClothing/arena_coats_dv.yml`, `Textures/_Wega/Clothing/OuterClothing/Coats/cybersunwindbreaker.rsi` |
| [Delta-V](https://github.com/DeltaV-Station/Delta-v) | `CC-BY-SA-3.0` | Security cap, detective's beret | `Entities/Clothing/Head/arena_hats_dv.yml`, `Textures/_Wega/Clothing/Head/Hats/{cap_sec,beret_det}.rsi` |
| [Delta-V](https://github.com/DeltaV-Station/Delta-v) | `CC-BY-SA-4.0` | Black turtleneck | `Entities/Clothing/Uniforms/arena_uniforms_dv.yml`, `Textures/_Wega/Clothing/Uniforms/Jumpsuit/black_turtleneck.rsi` |
| [Delta-V](https://github.com/DeltaV-Station/Delta-v) | `CC0-1.0` | Senior officer's turtleneck, white security jumpsuit | `Entities/Clothing/Uniforms/arena_uniforms_dv.yml`, `Textures/_Wega/Clothing/Uniforms/Jumpsuit/{security_alt,security_white}.rsi` |
| [Delta-V](https://github.com/DeltaV-Station/Delta-v) | inherited from base | Chameleon armor vest | `Entities/Clothing/OuterClothing/arena_armor.yml` |
| [Frontier Station](https://github.com/new-frontiers-14/frontier-station-14) | `CC-BY-SA-4.0` | Gladiator armor, bogu, tousei-gusoku | `Entities/Clothing/OuterClothing/arena_armor_frontier.yml`, `Textures/_Wega/Clothing/OuterClothing/Armor/{gladiator,bogu,touseigusoku}.rsi` |
| [Frontier Station / Nyanotrasen](https://github.com/new-frontiers-14/frontier-station-14) | `CC-BY-SA-3.0` / `CC-BY-SA-4.0` | Kendo men, kabuto and menpo helmets | `Entities/Clothing/Head/arena_hats_ny.yml`, `Textures/_Wega/Clothing/Head/Helmets/{men,kabuto}.rsi` |
| [Frontier Station / Nyanotrasen](https://github.com/new-frontiers-14/frontier-station-14) | `CC-BY-SA-3.0` / `CC-BY-SA-4.0` | Mail carrier, t-shirt/jeans, prison guard uniforms | `Entities/Clothing/Uniforms/arena_uniforms_ny.yml`, `Textures/_Wega/Clothing/Uniforms/Jumpsuit/{mailman_ny,tshirtjeans_ny,prisonguard_ny}.rsi`, `Textures/_Wega/Clothing/Uniforms/Jumpskirt/mailman_ny.rsi` |
| [Frontier Station](https://github.com/new-frontiers-14/frontier-station-14) | `CC-BY-SA-3.0` | Basic reflective vest | `Entities/Clothing/OuterClothing/arena_armor_frontier.yml`, `Textures/_Wega/Clothing/OuterClothing/Armor/basic_reflective_vest.rsi` |
| [Frontier Station](https://github.com/new-frontiers-14/frontier-station-14) | `CC-BY-SA-3.0` | Bounty hunter's flak trenchcoat | `Entities/Clothing/OuterClothing/arena_coats_frontier.yml`, `Textures/_Wega/Clothing/OuterClothing/Coats/bounty_hunter_coat.rsi` |
| [Frontier Station](https://github.com/new-frontiers-14/frontier-station-14) | `CC-BY-SA-3.0` / `CC-BY-SA-4.0` | Bishop's robes, witch hunter's coat, brick/fake wizard coats | `Entities/Clothing/OuterClothing/arena_coats_frontier.yml`, `Textures/_Wega/Clothing/OuterClothing/Coats/{bishop_robe,witch_hunter_coat,brickwizard,wizard-fake}.rsi` |
| [Einstein Engines](https://github.com/Simple-Station/Einstein-Engines) | `CC-BY-SA-3.0` | SWAT suit | `Entities/Clothing/OuterClothing/arena_armor_ee.yml`, `Textures/_Wega/Clothing/OuterClothing/Armor/swat.rsi` |
| [Metro 14](https://github.com/Metro-Observers/metro-14-official) | `CC-BY-SA-3.0` | Burning barrel, rusted/blue/yellow storage barrels, radioactive/fuel/toxin barrels | `Entities/Structures/Decoration/arena_decoration_metro.yml`, `Textures/_Wega/Structures/Decoration/{firebarrel,barrels}.rsi` |

### ID перенесённых прототипов

**Декор / постройки:**
- `BurningBarrelWega`
- `BarrelRustedWega`
- `BarrelBlueWega`
- `BarrelYellowWega`
- `BarrelRadiationWasteWega`
- `BarrelFuelWega`
- `BarrelToxinWega`

**Головные уборы / шлемы:**
- `ClothingHeadHelmetKendoMenWega`
- `ClothingHeadHelmetKabutoWega`
- `ClothingHeadHatCapSecWega`
- `ClothingHeadHatBeretDetWega`

**Броня / жилеты:**
- `ClothingOuterArmorPlateCarrierWega`
- `ClothingOuterArmorDuraVestWega`
- `ClothingOuterArmorARCWega`
- `ClothingOuterVestFlakWega`
- `ClothingOuterVestPlateCarrierAdvWega`
- `ClothingOuterChameleonArmorWega`
- `ClothingOuterArmorGladiatorWega`
- `ClothingOuterArmorKendoBoguWega`
- `ClothingOuterArmorTouseiGusokuWega`
- `NFClothingOuterArmorReflectiveWega`
- `ClothingOuterArmorSwatWega`

**Верхняя одежда:**
- `ClothingOuterCoatCybersunWindbreakerWega`
- `ClothingOuterCoatBHTrenchWega`
- `ClothingOuterCoatBishopWega`
- `ClothingOuterCoatWitchHunterWega`
- `ClothingOuterWizardBrickWega`
- `ClothingOuterFakeWizardWega`

**Униформа:**
- `ClothingUniformBlackTurtleneckWega`
- `ClothingUniformJumpsuitSecTurtleWega`
- `ClothingUniformJumpsuitSecWhiteWega`
- `ClothingUniformJumpsuitMailCarrierWega`
- `ClothingUniformJumpskirtMailCarrierWega`
- `ClothingUniformJumpsuitTshirtJeansWega`
- `ClothingUniformJumpsuitTshirtJeansGrayWega`
- `ClothingUniformJumpsuitTshirtJeansPeachWega`
- `ClothingUniformJumpsuitPrisonGuardWega`

**Оружие ближнего боя:**
- `AdvancedTruncheonWega`
- `SilverSwordWega`

## Рекомендуемые репозитории

### 1. Delta-V (`DeltaV-Station/Delta-v`)
- **Лицензия:** в основном `CC0-1.0` / `CC-BY-SA-3.0`; проверяйте `meta.json` каждого RSI.
- **Что брать:**
  - Броня и разгрузки (`_DV/Entities/Clothing/OuterClothing/Armor/`).
  - Гражданская одежда (`_DV/Entities/Clothing/Uniforms/`, `Shoes/`, `Head/`).
  - Шлемы и маски для «операторского» вида.
  - Нестандартные стены и декорации (`_DV/Entities/Structures/`).
- **Плюсы:** чистые спрайты, понятная структура, лицензии обычно разрешены.

### 2. Frontier Station (`new-frontiers-14/frontier-station-14`)
- **Лицензия:** `CC-BY-SA-3.0` / `CC0-1.0` в зависимости от файла.
- **Что брать:**
  - «Фронтирные» костюмы, бронежилеты, тактические жилеты.
  - Постройки для баз/лагерей: палатки, баррикады, контейнеры.
  - Тайлы песка/земли для «уличных» арен.
- **Плюсы:** много контента для выживания и баз, отлично ложится на постапокалиптические арены.

### 3. Einstein Engines (`Simple-Station/Einstein-Engines`)
- **Лицензия:** `CC-BY-SA-3.0`.
- **Что брать:**
  - Униформа наемников/ЧВК.
  - Дополнительные виды брони и снаряжения.
- **Минусы:** меньше уникальных структур, больше фокус на одежде.

### 4. Metro 14 (`Metro-Observers/metro-14-official`)
- **Лицензия:** `CC-BY-SA-3.0`.
- **Что брать:**
  - Постапокалиптический декор: бочки, костры, мусор, баррикады.
  - Разноцветные бочки-хранилища и радиоактивные/горящие варианты.
- **Плюсы:** отличная эстетика для «уличных» и подземных арен.

### 5. SS220 / Corvax-совместимые сборки
- **Лицензия:** варьируется, часто `CC-BY-SA-3.0`.
- **Что брать:**
  - Русскоязычные предметы, локализованные названия.
  - Национальная/постсоветская эстетика: берцы, гопник-одежда, маски.
- **Важно:** проверять лицензию на каждый RSI — не весь контент открытый.

### 5. Lust Station / Adventure Time / другие русские форки
- **Лицензия:** часто непрозрачная, много портов из SS13 без чёткой лицензии.
- **Рекомендация:** использовать только если есть явная `CC0` / `CC-BY-SA` в `meta.json` или README.

## Что искать под арену

| Категория | Где обычно лежит | Примечания |
|---|---|---|
| **Броня** | `*/Entities/Clothing/OuterClothing/Armor/` | Проверять баланс, parent'ы и теги. |
| **Униформа** | `*/Entities/Clothing/Uniforms/` | Хорошо для фракций/скинов. |
| **Шлемы/маски** | `*/Entities/Clothing/Head/`, `Mask/` | Визуальный разделитель команд. |
| **Постройки/укрытия** | `*/Entities/Structures/Walls/`, `Furniture/`, `Barricades/` | Для маппинга арены. |
| **Тайлы** | `*/Tiles/` | Асфальт, песок, бетон, металл. |
| **Оружие ближнего боя** | `*/Entities/Objects/Weapons/Melee/` | Ножи, мечи, дубинки. |
| **Стрелковое оружие** | `*/Entities/Objects/Weapons/Guns/` | Требует калибров и баллистики проекта. |
| **Декор/пропы** | `*/Entities/Structures/Decoration/`, `Objects/Decorations/` | Бочки, ящики, мусор — атмосфера. |

## Процесс переноса

1. **Проверить лицензию** в `meta.json` и в README репозитория.
2. **Скачать** `.yml` прототип и соответствующий `.rsi`.
3. **Адаптировать пути:** заменить `*/_DV/...` / `*/_NF/...` на `_Wega/...`.
4. **Адаптировать ID:** добавить суффикс `Wega` или `Arena`, чтобы не было конфликтов с upstream.
5. **Проверить parent'ы:** убедиться, что базовые прототипы (`ClothingOuterBaseMedium`, `AllowSuitStorageClothing` и т.д.) есть в wega-mega.
6. **Добавить `ru-RU` локализацию:** `Resources/Locale/ru-RU/...`.
7. **Проверить `Content.YAMLLinter`:** `dotnet run --project Content.YAMLLinter --no-build`.
8. **Если добавляете в арсенал:** синхронизировать `full_arsenal_pool.yml`, `melee_arsenal_pool.yml` и соответствующие `.md` прайс-листы (см. `AGENTS.md`).

## Чего избегать

- **Nuclear-14** — много крутого постапокалиптического контента, но текстуры под `CC-BY-NC-SA-3.0` (non-commercial), поэтому не подходят.
- Репозитории без чёткой лицензии или с портами «как есть» из коммерческих игр.
- Контент с `All rights reserved` / `Do not redistribute`.
