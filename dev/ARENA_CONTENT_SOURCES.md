# Источники внешнего контента для арен и пострелушек

Документ собирает SS14-репозитории с контентом, который можно использовать при маппинге арен, создании лута и экипировки для PvP/PvE-режимов.

> ⚠️ Перед использованием любого контента проверяйте лицензию и требования к атрибуции. Большинство SS14-форков распространяются под AGPL/MIT/CC-BY-SA-3.0, но условия могут отличаться.

---

## Быстрый выбор по задаче

| Что нужно | Куда смотреть первым делом |
|-----------|---------------------------|
| Тяжёлая броня, силовая броня, постапок | [Nuclear-14](#nuclear-14) |
| Тактическая броня, бронежилеты, тайлы | [Delta-V](#delta-v) |
| Готовые арены, шаттлы, данжи | [Frontier Station](#frontier-station-14) |
| Фракционная/корпоративная одежда | [Einstein Engines](#einstein-engines) |
| Агрегатор контента из множества форков | [TheDen](#theden), [Impstation](#impstation) |

---

## Nuclear-14

**Репозиторий:** https://github.com/Vault-Overseers/nuclear-14  
**Тематика:** Fallout — постапокалипсис, силовая броня, лазеры, разрушенные структуры.

### Силовая броня

Прототипы:
- `N14ClothingOuterPowerArmorT45`
- `N14ClothingOuterPowerArmorT51`
- `N14ClothingOuterPowerArmorT60`
- `N14ClothingOuterPowerArmorT60Tesla`
- `N14ClothingOuterPowerArmorAdvanced1` (X-01)

Файлы:
- `Resources/Prototypes/_Nuclear14/Entities/Clothing/OuterClothing/powerarmor.yml`
- `Resources/Textures/_Nuclear14/Clothing/OuterClothing/PowerArmor/{t45,t51,t60,t60tesla,advanced1}.rsi`

Шлемы:
- `N14ClothingHeadHelmetPowerArmorT45`
- `N14ClothingHeadHelmetPowerArmorT51`
- `N14ClothingHeadHelmetPowerArmorT60`

### Лёгкая и средняя броня

- `N14ClothingOuterZealotDuster`
- `N14ClothingScavengerHeavyArmor`
- `N14ClothingOuterCoatFollowersArmored`
- `N14ClothingOuterLeatherArmor`
- `N14ClothingOuterVestLeather`

Файл: `Resources/Prototypes/_Nuclear14/Entities/Clothing/OuterClothing/falloutarmor.yml`

### Оружие

Полный набор вооружения:
- `Resources/Prototypes/_Nuclear14/Entities/Objects/Weapons/Guns/Pistols/`
- `Resources/Prototypes/_Nuclear14/Entities/Objects/Weapons/Guns/Revolvers/`
- `Resources/Prototypes/_Nuclear14/Entities/Objects/Weapons/Guns/Rifles/`
- `Resources/Prototypes/_Nuclear14/Entities/Objects/Weapons/Guns/Shotguns/`
- `Resources/Prototypes/_Nuclear14/Entities/Objects/Weapons/Guns/SMGs/`
- `Resources/Prototypes/_Nuclear14/Entities/Objects/Weapons/Guns/Snipers/`
- `Resources/Prototypes/_Nuclear14/Entities/Objects/Weapons/Guns/Heavy/`
- `Resources/Prototypes/_Nuclear14/Entities/Objects/Weapons/Guns/Battery/` (лазеры)
- `Resources/Prototypes/_Nuclear14/Entities/Objects/Weapons/Guns/Flamers/`

### Структуры, тайлы, декали

- Стены, двери, окна, мебель, машины: `Resources/Prototypes/_Nuclear14/Entities/Structures/`
- Тайлы пола/воды: `Resources/Prototypes/_Nuclear14/Tiles/`
- Декали дорог, граффити: `Resources/Textures/_Nuclear14/Decals/`

---

## Delta-V

**Репозиторий:** https://github.com/DeltaV-Station/Delta-v  
**Тематика:** продолжение Nyanotrasen — тактическая экипировка, стильные тайлы, разнообразная броня.

### Броня

- `ClothingOuterArmorPlateCarrier` — бронежилет, хорош против пуль
- `ClothingOuterArmorDuraVest` — защита от ударов/порезов
- `ClothingOuterArmorARC` — тяжёлый костюм подавления бунтов

Файл: `Resources/Prototypes/_DV/Entities/Clothing/OuterClothing/armor.yml`

Также интересны:
- `Resources/Prototypes/_DV/Entities/Clothing/OuterClothing/hardsuits.yml`
- `Resources/Prototypes/_DV/Entities/Clothing/OuterClothing/vests.yml`
- `Resources/Prototypes/_DV/Entities/Clothing/OuterClothing/longcoats/`

### Тайлы

Большая коллекция стальных, цветных и повреждённых полов:
- `Resources/Textures/_DV/Tiles/`

### Структуры

- `Resources/Textures/_DV/Structures/Walls/`
- `Resources/Textures/_DV/Structures/Doors/`
- `Resources/Textures/_DV/Structures/Furniture/`
- `Resources/Textures/_DV/Structures/Machines/`

---

## Frontier Station 14

**Репозиторий:** https://github.com/new-frontiers-14/frontier-station-14  
**Тематика:** космическое выживание, шаттлы, торговые постройки, готовые локации.

### Готовые карты

- Арена: `Resources/Maps/_NF/POI/arena.yml`
- Локации для боёв/выживания:
  - `Resources/Maps/_NF/POI/cove.yml`
  - `Resources/Maps/_NF/POI/lodge.yml`
  - `Resources/Maps/_NF/POI/grifty.yml`
- Данжи:
  - `Resources/Maps/_NF/Dungeon/lava_mercenary.yml`
  - `Resources/Maps/_NF/Dungeon/mineshaft.yml`
  - `Resources/Maps/_NF/Dungeon/wreck.yml`
- Шаттлы (можно адаптировать как базы/арены):
  - `Resources/Maps/_NF/Shuttles/`

### Контент

- Шаттловые структуры: `Resources/Textures/_NF/Structures/Shuttles/`
- Одежда/броня фронтира: `Resources/Textures/_NF/Clothing/`

---

## Einstein Engines

**Репозиторий:** https://github.com/Simple-Station/Einstein-Engines  
**Тематика:** RP/hard fork с корпоративными фракциями и расширенным контентом.

### Корпоративная фракционная одежда

Отлично подходит для командных боёв корпораций:
- Biesel Republic
- Hephaestus
- NanoTrasen
- Sol Alliance
- Zavodskoi
- Idris Incorporated
- Orion Express

Путь: `Resources/Textures/_EE/Clothing/`

---

## TheDen

**Репозиторий:** https://github.com/TheDenSS14/TheDen

Агрегирует контент из множества форков в подпапках:
- `_DV`, `_NF`, `_Nuclear14`, `_RMC14`, `_EE`, `_CD`, `_Impstation`, `_Starlight`

Удобно как «витрина» — можно быстро посмотреть, что уже адаптировано для переноса.

---

## Impstation

**Репозиторий:** https://github.com/impstation/imp-station-14

Ещё один агрегатор с явно прописанной таблицей атрибуции. Подпапки:
- `_Impstation`, `_CD`, `_Corvax`, `_DV`, `_EE`, `_DEN`, `_NF`, `_Nuclear14`

См. `README.md` репозитория для точной таблицы лицензий и источников.

---

## Как портировать контент

1. **Скопируйте** нужные `.rsi` и `.yml` в ваш namespace (например, `_Wega/`).
2. **Переименуйте пути** в прототипах с `_Nuclear14/`, `_DV/`, `_NF/` на `_Wega/`.
3. **Проверьте зависимости** — убедитесь, что используемые компоненты и прототипы-родители существуют в вашем билде.
4. **Проверьте лицензию** и добавьте атрибуцию, если требуется.
5. **Протестируйте** загрузку прототипов через YAMLLinter или интеграционный тест.

---

## Что уже есть в wega-mega

В проекте уже интегрированы:
- `_RMC14` — военная тематика, оружие, броня
- `_Starlight` — дополнительный контент
- `_Sunrise` — одежда, структуры
- `Corvax` — русскоязычный контент

Перед портированием из внешних репозиториев проверяйте, нет ли похожего контента уже в этих namespace.
