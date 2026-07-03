# Дуэльный ящик снаряжения — Supply Drop

Спавнится **в центре арены** каждые **45 секунд**, исчезает через **44 секунды**.  
При появлении: **колокол** слышен всем на станции, ящик **светится оранжевым**.

---

## Механика

Ящик содержит **4 независимых слота** (orGroup). Каждый слот выдаёт **ровно один** предмет из своего пула — все варианты равновероятны (`prob: 1`).

| Слот | Содержимое | Пустой |
|------|-----------|--------|
| **Medicine** | Боевой медицинский предмет | никогда |
| **Ammo** | Пачка боеприпасов | никогда |
| **Utility** | Зарядник / дымовуха / стимпак | ~17% |
| **Melee** | Холодное оружие | ~14% |

---

## Medicine — Медицина (26 вариантов)

### Медипены

| Предмет | Эффект |
|---------|--------|
| `EmergencyMedipen` | Бикаридин + дексалин + эпинефрин |
| `SurvivalMedipen` | Трикордразин + келотан |
| `CombatMedipen` | Бикаридин + стимуляторы |

### Аптечки

| Предмет | Содержимое |
|---------|-----------|
| `MedkitCombatFilled` | Боевая — брут + ожог |
| `MedkitBruteFilled` | Физические травмы |
| `MedkitBurnFilled` | Ожоги |
| `MedkitAdvancedFilled` | Продвинутая — всё |

### Перевязочные материалы

| Предмет | Эффект |
|---------|--------|
| `Brutepack` | Лечит физические травмы |
| `Ointment` | Лечит ожоги |
| `Gauze` | Останавливает кровотечение |
| `MedicatedSuture` | Продвинутые швы |
| `RegenerativeMesh` | Ускоренное заживление |

### Шприцы

| Предмет | Реагент |
|---------|---------|
| `SyringeBicaridine` | Бикаридин — травмы |
| `SyringeDermaline` | Дермалин — ожоги |
| `SyringeEphedrine` | Эфедрин — стимулятор |
| `SyringeStimulants` | Стимуляторы — бой |
| `SyringeTranexamicAcid` | Транексамовая кислота — кровотечение |

### Таблетки

| Предмет | Реагент |
|---------|---------|
| `PillCanisterBicaridine` | Бикаридин — травмы |
| `PillCanisterBruzin` | Брузин — ушибы |
| `PillCanisterDermaline` | Дермалин — ожоги |
| `PillCanisterExMedipen` | Расширенный медипен |
| `PillCanisterKelotane` | Келотан — ожоги |
| `PillCanisterLacerinol` | Лацеринол — порезы |
| `PillCanisterOmnizine` | Омнизин — все виды урона |
| `PillCanisterTricordrazine` | Трикордразин — травмы + ожоги |

---

## Ammo — Боеприпасы (21 вариант)

| Предмет | Тип |
|---------|-----|
| `MagazineBoxPistol` | Пистолетные |
| `MagazineBoxPistolIncendiary` | Пистолетные зажигательные |
| `MagazineBoxPistolUranium` | Пистолетные урановые |
| `MagazineBoxMagnum` | Магнум |
| `MagazineBoxMagnumAP` | Магнум бронебойные |
| `MagazineBoxMagnumIncendiary` | Магнум зажигательные |
| `MagazineBoxMagnumUranium` | Магнум урановые |
| `MagazineBoxRifle` | Винтовочные .20 |
| `MagazineBoxRifleBig` | Винтовочные .20 (крупная пачка) |
| `MagazineBoxRifleIncendiary` | Винтовочные зажигательные |
| `MagazineBoxRifleUranium` | Винтовочные урановые |
| `MagazineBoxLightRifle` | Лёгкая винтовка .30 |
| `MagazineBoxLightRifleBig` | Лёгкая винтовка .30 (крупная пачка) |
| `MagazineBoxLightRifleIncendiary` | Лёгкая винтовка зажигательные |
| `MagazineBoxLightRifleUranium` | Лёгкая винтовка урановые |
| `MagazineBoxCaselessRifle` | Безгильзовая винтовка |
| `MagazineBoxAntiMateriel` | Противоматериальные |
| `BoxLethalshot` | Дробь летальная |
| `BoxShotgunSlug` | Пуля для дробовика |
| `BoxShotgunIncendiary` | Зажигательная картечь |
| `BoxShotgunUranium` | Урановая картечь |

---

## Utility — Утилита (~83% шанс, 5 вариантов)

| Предмет | Эффект |
|---------|--------|
| `SmokeGrenade` | Дымовая граната |
| `Flash` | Вспышка — ослепляет |
| `Stimpack` | Стимпак (гиперзин, полный) |
| `StimpackMini` | Микроинъектор гиперзина |
| `PortableRecharger` | Переносной зарядник для энергооружия |

> ~17% — слот пустой.

---

## Melee — Холодное оружие (~86% шанс, 5 вариантов)

| Предмет | Особенность |
|---------|-------------|
| `CombatKnife` | Быстрый |
| `Machete` | Высокий урон |
| `Spear` | Колющий, длинная рука |
| `SurvivalKnife` | Универсальный |
| `EnergyDagger` | Энергетический, не нужны патроны |

> ~14% — слот пустой.
