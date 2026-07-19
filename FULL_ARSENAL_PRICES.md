# Full Arsenal Crate — Список предметов и цены

Ящик `CrateSyndicateFullArsenal`. Бюджет: **40 TC**. Контекст: **арена, дуэль 1 на 1**.

Лимиты категорий за одно открытие:
- Броня (`FullArsenalArmor`): **макс. 2**
- Шлемы/противогазы (`FullArsenalHead`): **макс. 2**
- Гранаты (`FullArsenalGrenade`): **макс. 3**
- Патроны (`FullArsenalAmmo`): **макс. 4**
- Пустые сумки/рюкзаки/вещмешки (`FullArsenalBag`): **макс. 1**
- Наборы лучника (`FullArsenalBowSet`): **макс. 1** (любой из шести вариантов, включая кубик лучника)
- Инжекторы (`FullArsenalInjector`): **макс. 1** (гипоспрей/гипоручка/гиподротик; к инжектору гарантированно докидывается минимум одна бутылочка с лекарством) (готовые комплекты-вещмешки с оружием/скафандрами сюда не входят)

Гарантии каждого ящика хаоса (все три размера):
- минимум **один огнестрел** (`guaranteedGun`) — чисто-мелешных открытий не бывает;
- минимум **одна броня** (`guaranteedCategories: FullArsenalArmor`);
- патроны **не гарантируются**: они выпадают только под уже выпавший совместимый ствол (`requireGunForAmmo`), а совместимые с ним идут с весом ×4 (`ammoAffinityMultiplier`);
- «весомые» предметы ≥8 ТК: 1 шт. в обычном, 2 в продвинутом, 3 (≥10 ТК) в мега.

Броня и шлемы зафиксированы (2 / 2) во всех четырёх ящиках — с размером ящика они не растут.
Увеличенные варианты (тот же пул, отдельные текстуры) масштабируют только гранаты/патроны:
- `CrateSyndicateFullArsenalPlus` (аплинк **60 TC**) — бюджет до 60 TC; броня 2 / шлемы 2 / гранаты 5 / патроны 6.
- `CrateSyndicateFullArsenalMega` (аплинк **120 TC**) — бюджет до 120 TC; броня 2 / шлемы 2 / гранаты 9 / патроны 12.

⚠️ В любой ящик влезает **не более 30 предметов** (`EntityStorage.Capacity`). Генерация набора (`SurplusBundle.MaxItems = 30`) останавливается на 30 предметах, даже если бюджет не израсходован — поэтому большие ящики не всегда тратят весь лимит ТК, но и не сыплют лишнее на пол.

Цены калиброваны по аплинку: предметы из аплинка = цена аплинка; предметы из бандлей ≈ цена бандля минус ~2 TC за упаковку.

---

## Ближний бой

| Предмет | Entity | Цена |
|---|---|---|
| Голо-кирка | `EnergyPickaxe` | 5 TC |
| Бейсбольная бита | `BaseBallBat` | 1 TC |
| Копьё | `Spear` | 1 TC |
| Костяное копьё | `SpearBone` | 1 TC |
| Усиленное копьё | `SpearReinforced` | 2 TC |
| Плазменное копьё | `SpearPlasma` | 3 TC |
| Урановое копьё | `SpearUranium` | 3 TC |
| Копьё из зуба акулы-мальки | `SpearSharkMinnow` | 3 TC |
| Глефа-дробилка | `WeaponCrusherGlaive` | 5 TC |
| Магмитовая глефа-дробилка | `WeaponMagmiteCrusherGlaive` | 6 TC |
| Магмитовый крушитель | `WeaponMagmiteCrusher` | 6 TC |
| Мачете | `Machete` | 1 TC |
| Пожарный топор | `FireAxe` | 1 TC |
| Пылающий пожарный топор | `FireAxeFlaming` | 4 TC |
| Багет | `WeaponBaguette` | 2 TC |
| Боевой нож | `CombatKnife` | 1 TC |
| Катласс | `Cutlass` | 2 TC |
| Энергетический кинжал | `EnergyDagger` | 2 TC |
| Катана | `Katana` | 3 TC |
| Энергокатана ниндзя | `EnergyKatana` | 9 TC |
| Сабля капитана (с ножнами) | `ClothingBeltSheathFilled` | 3 TC |
| Проклятая катана | `WeaponCursedKatana` | 4 TC |
| Клеймор | `Claymore` | 4 TC |
| Нулевой жезл (с ножнами) | `ClothingBeltSheathChaplinFilled` | 2 TC |
| Силовой меч | `WeaponForceSword` | 5 TC |
| Дробилка | `WeaponCrusher` | 5 TC |
| Цепной меч | `WeaponChainsword` | 6 TC |
| Сталь Хандзо | `WeaponHanzoSteel` | 6 TC |
| Клинок смерти | `WeaponDeathBlade` | 7 TC |
| Высокочастотный клинок | `WeaponHighFrequencyBlade` | 7 TC |
| Энергетический меч | `EnergySword` | 8 TC |
| Двойной энергетический меч | `EnergySwordDouble` | 20 TC |
| Адский клинок | `ArenaHellBlade` | 4 TC |
| Кровавый клинок | `ArenaBloodBlade` | 5 TC |
| Нереальный меч | `WeaponUnrealSword` | 5 TC |
| Рапира Синдиката (с ножнами) | `ClothingBeltSheathSyndicateFilled` | 8 TC |
| Металлическая пила | `Saw` | 1 TC |
| Дисковая пила Синдиката | `SawAdvanced` | 2 TC |
| Мясницкий тесак | `ButchCleaver` | 2 TC |
| Боевой секач | `WeaponCleawingSaw` | 3 TC |
| Метательный нож | `ThrowingKnife` | 2 TC |
| Боевой гвоздодёр | `CombatCrowbar` | 2 TC |
| Нож выживальщика | `SurvivalKnife` | 2 TC |
| Кукри | `KukriKnife` | 3 TC |
| Клинок-трость | `CaneSheathFilled` | 3 TC |
| Бензопила | `Chainsaw` | 4 TC |
| Зелёная бензопила | `ChainsawGreen` | 4 TC |
| Кувалда | `Sledgehammer` | 4 TC |
| Потусторонний клинок | `EldritchBlade` | 4 TC |
| Экстрапространственный клинок | `WeaponMultiverseSword` | 4 TC |
| Нечестивая алебарда | `UnholyHalberd` | 5 TC |
| Энергетический кинжал (видимый) | `EnergyDaggerLoud` | 6 TC |
| Энергетическая сабля | `EnergyCutlass` | 7 TC |
| Клинок смерти (арена) | `WeaponDeathBladeArena` | 7 TC |
| Ритуальный кинжал смерти (арена) | `WeaponDeathDaggerArena` | 3 TC |
| Лезвие руки | `ArmBlade` | 8 TC |
| Молот сингулярности | `SingularityHammer` | 12 TC |
| Мьёльнир | `Mjollnir` | 18 TC |
| Ратварианское копьё | `WeaponRatvarianSpearArena` | 4 TC |
| Заводной меч | `WeaponClockworkSwordArena` | 5 TC |
| Заводной меч (тяжёлый) | `WeaponClockworkSwordBorg` | 5 TC |
| Заводной молот | `WeaponClockHammerArena` | 6 TC |
| Заводной меч (Мечник) | `WeaponClockworkSwordSwordsmenArena` | 7 TC |
| Заводной меч (Кровопролитие) | `WeaponClockworkSwordBloodshedArena` | 7 TC |
| Ратварианское копьё (Дезориентация) | `WeaponRatvarianSpearConfusionArena` | 6 TC |
| Ратварианское копьё (Электрический разряд) | `WeaponRatvarianSpearElectricalTouchArena` | 6 TC |
| Заводной молот (Отброс) | `WeaponClockHammerKnockbackArena` | 8 TC |
| Заводной молот (Дробление) | `WeaponClockHammerCrusherArena` | 8 TC |
| Заводной молот (Демонтаж) | `WeaponClockHammerDismantlingArena` | 8 TC |

---

## Пистолеты и револьверы

| Предмет | Entity | Цена |
|---|---|---|
| Флинтлок | `WeaponPistolFlintlock` | 2 TC |
| Пистолет Mk58 | `WeaponPistolMk58` | 2 TC |
| Пистолет Viper | `WeaponPistolViper` | 2 TC |
| M1911 | `WeaponPistolM1911` | 3 TC |
| Glock-22 Auto | `WeaponPistolG22Auto` | 4 TC |
| Stechkin | `WeaponPistolStechkin` | 3 TC |
| Водяной пистолет | `WeaponWaterPistol` | 5 TC |
| Водяной бластер | `WeaponWaterBlaster` | 7 TC |
| Водяной супер-бластер | `WeaponWaterBlasterSuper` | 14 TC |
| Набор «Не грози южному централу» | `ClothingBackpackDuffelHoodStarter` | 6 TC |
| Снаб-револьвер | `WeaponRevolverSnubRevolver` | 3 TC |
| Синий лазерный пистолет | `WeaponBlueLaserPistol` | 3 TC |
| Пистолет N1984 | `WeaponPistolN1984` | 8 TC |
| Пистолет Cobra | `WeaponPistolCobra` | 3 TC |
| Антикварный лазер | `WeaponAntiqueLaser` | 3 TC |
| Лазерный пистолет (HoS) | `WeaponLaserGun` | 4 TC |
| Пистолет CHIMP | `WeaponPistolCHIMP` | 4 TC |
| Пульсар | `WeaponEnergyPulsar` | 4 TC |
| Револьвер Python | `WeaponRevolverPython` | 4 TC |
| Инспектор | `WeaponRevolverInspector` | 3 TC |
| Револьвер Mateba | `WeaponRevolverMateba` | 4 TC |
| Револьвер «Spearhead» (чёрный) | `WeaponRevolverSpearheadBlack` | 5 TC |
| Улучшенный лазер | `WeaponAdvancedLaser` | 5 TC |
| Энергетический арбалет | `WeaponEnergyCrossbow` | 5 TC |
| Энергетический арбалет (крупный) | `WeaponEnergyCrossbowLarge` | 8 TC |
| S-13 «Чёрная мамба» (плазменный, полный авто) | `WeaponSyndieLaserGun` | 38 TC |
| Револьвер Python AP | `WeaponRevolverPythonAP` | 6 TC |
| Revolving Mateba AP | `WeaponRevolverMatebaAP` | 8 TC |
| Desert Eagle | `WeaponPistolDesertEagle` | 4 TC |
| Desert Eagle AP | `WeaponPistolDesertEagleAP` | 6 TC |
| Dominator (три режима) | `WeaponDominator` | 8 TC |
| Импульсный пистолет | `WeaponPulsePistol` | 30 TC |
| Пиратский пистолет | `WeaponRevolverPirate` | 2 TC |
| Deckard | `WeaponRevolverDeckard` | 4 TC |
| Энергетический магнум | `WeaponEnergyMagnum` | 5 TC |
| Пистолет Echis | `WeaponPistolEchis` | 5 TC |

---

## Пистолеты-пулемёты

| Предмет | Entity | Цена |
|---|---|---|
| ПП Дрозд | `WeaponSubMachineGunDrozd` | 4 TC |
| Scorpion VZ-61 | `WeaponSubMachineGunSkorpion` | 4 TC |
| MP5 | `WeaponSubMachineGunMP5` | 8 TC |
| ПП WT550 | `WeaponSubMachineGunWt550` | 5 TC |
| ПП Berkut | `WeaponSubmachinegunBerkut` | 6 TC |
| ПП Atreides | `WeaponSubMachineGunAtreides` | 18 TC |
| ПП C-20r | `WeaponSubMachineGunC20r` | 10 TC |
| MP-38 | `WeaponSubMachineGunMP38` | 3 TC |
| MP7 | `WeaponSubMachineGunMP7` | 10 TC |
| P-90 | `WeaponSubMachineGunP90` | 25 TC |
| PPSH-59 | `WeaponSubMachineGunPPSH` | 10 TC |
| SP-91-RC | `WeaponSubMachineGunSP91RC` | 6 TC |
| Узи U3 | `SunriseWeaponSubMachineGunUzi` | 5 TC |
| SIAR-52 (безгильзовый магазин) | `WeaponSIAR52Arena` | 10 TC |
| AJ-100 (универсальный разъём, рандомный магазин) | `WeaponAJ100Arena` | 10 TC |

---

## Дробовики

| Предмет | Entity | Цена |
|---|---|---|
| Самодельный дробовик | `WeaponShotgunHandmade` | 1 TC |
| Trenchgun L-4034 | `WeaponShotgunTrenchgun4034` | 4 TC |
| Spas-12 | `WeaponShotgunSPAS12` | 5 TC |
| Двустволка | `WeaponShotgunDoubleBarreled` | 2 TC |
| Двустволка (резина) | `WeaponShotgunDoubleBarreledRubber` | 2 TC |
| Kammerer | `WeaponShotgunKammerer` | 3 TC |
| Enforcer (резина) | `WeaponShotgunEnforcerRubber` | 4 TC |
| Enforcer | `WeaponShotgunEnforcer` | 5 TC |
| Hushpup | `WeaponShotgunHushpup` | 5 TC |
| Bulldog | `WeaponShotgunBulldog` | 9 TC |
| AS-12 «Минотавр» | `WeaponShotgunMinotaur` | 12 TC |
| Самодельное ружьё | `WeaponShotgunImprovisedLoaded` | 1 TC |
| Мушкетон | `WeaponShotgunBlunderbuss` | 2 TC |
| Обрез | `WeaponShotgunSawn` | 2 TC |
| Энергетический дробовик | `WeaponEnergyShotgun` | 5 TC |
| Револьвер Tkach GA-12 | `WeaponShotgunGA12` | 4 TC |

---

## Винтовки, снайперки, пулемёты

| Предмет | Entity | Цена |
|---|---|---|
| Снайперка Мосин | `WeaponSniperMosin` | 1 TC |
| Импровизированный лазер | `WeaponMakeshiftLaser` | 3 TC |
| Сигнальный пистолет | `WeaponFlareGun` | 2 TC |
| Сигнальный пистолет (охрана) | `WeaponFlareGunSecurity` | 3 TC |
| Лазерный карабин | `WeaponLaserCarbine` | 3 TC |
| Сойка | `WeaponRifleJay` | 10 TC |
| Винтовка AK | `WeaponRifleAk` | 6 TC |
| АК-514 | `WeaponRifleAK514` | 13 TC |
| SKM-24 | `WeaponRifleSKM24` | 8 TC |
| SKM-28 (снайперская, .308) | `WeaponRifleSKM28Syndi` | 10 TC |
| Bauer127 (противоматериальная, 15mm) | `WeaponRifleBauer127Arena` | 12 TC |
| Набор "Элитный Снайпер" (Bauer127 + 2×наборы магазинов + тепловизор) | `ClothingBackpackDuffelSyndicateFilledBauer127` | 20 TC |
| Ионный карабин | `WeaponIonCarabine` | 5 TC |
| Винтовка Lecter | `WeaponRifleLecter` | 5 TC |
| Снайперка Estoc | `WeaponRifleEstoc` | 5 TC |
| Лазер Svalinn | `WeaponLaserSvalinn` | 6 TC |
| Снайперка Hristov | `WeaponSniperHristov` | 8 TC |
| Пулемёт L6C (компакт) | `WeaponLightMachineGunL6C` | 10 TC |
| Снайперка Hristov МК2 | `WeaponSniperHristovAdvanced` | 12 TC |
| Пулемёт L6 | `WeaponLightMachineGunL6` | 12 TC |
| Пулемёт DP-28 | `WeaponLightMachineGunDP28` | 11 TC |
| Импульсный карабин | `WeaponPulseCarbine` | 35 TC |
| Импульсный дробовик | `WeaponPulseShotgun` | 46 TC |
| Импульсная снайперская винтовка | `WeaponPulseSniper` | 42 TC |
| Мушкет | `Musket` | 6 TC |
| xC-67 (одиночно) | `WeaponRifleXC67` | 7 TC |
| M-90gl (одиночно) | `WeaponRifleM90GrenadeLauncher` | 8 TC |
| Lecter XL8 | `WeaponRifleLecterXL8` | 8 TC |
<!-- | Болтер | `WeaponRifleBolter` | 10 TC | временно отключён -->
| M16A4 | `WeaponRifleM16A4` | 10 TC |
| AR2 (импульсная) | `WeaponRifleAR2` | 11 TC |
| ASH-12 | `WeaponRifleAsh12` | 12 TC |
| M-52 | `WeaponRifleM52` | 13 TC |
| Famas | `WeaponRifleFamas` | 9 TC |
| G-36 | `WeaponRifleG36` | 9 TC |

---

## Тяжёлое и специальное оружие

| Предмет | Entity | Цена |
|---|---|---|
| Температурный пистолет | `WeaponTemperatureGun` | 3 TC |
| Пушка Форс | `WeaponForceGun` | 5 TC |
| Замедлитель частиц | `WeaponParticleDecelerator` | 5 TC |
| Рентгеновская пушка | `WeaponXrayCannon` | 8 TC |
| Пневматическая пушка | `WeaponImprovisedPneumaticCannonFilled` | 2 TC |
| Тесла-пистолет | `WeaponTeslaGun` | 18 TC |
| Лазерная пушка | `WeaponLaserCannon` | 7 TC |
| Протокинетический ускоритель | `WeaponProtoKineticAccelerator` | 8 TC |
| Ракетница (РПГ-7) | `WeaponLauncherRocket` | 18 TC |

---

## Станнеры и дестабилизаторы

| Предмет | Entity | Цена |
|---|---|---|
| Электродубинка | `Stunbaton` | 3 TC |
| Дубинка | `Truncheon` | 2 TC |
| Телескопическая дубинка | `Telescopicbaton` | 3 TC |
| Стан-прод | `Stunprod` | 3 TC |
| Дубинка офицера Блюшилд | `StunbatonBlueshield` | 4 TC |
| Тазер | `WeaponTaser` | 4 TC |
| Дизаблер | `WeaponDisabler` | 3 TC |
| Супер-тазер | `WeaponTaserSuper` | 10 TC |
| Дизаблер-ПП | `WeaponDisablerSMG` | 5 TC |
| Кастет с оглушением | `ClothingHandsKnuckleDustersStun` | 5 TC |

---

## Щиты

| Предмет | Entity | Цена |
|---|---|---|
| Противоударный щит | `RiotShield` | 2 TC |
| Противолазерный щит | `RiotLaserShield` | 4 TC |
| Пулезащитный щит | `RiotBulletShield` | 4 TC |
| Телескопический щит | `TelescopicShield` | 5 TC |
| Тактический жилет Web (щит) | `ClothingOuterVestWeb` | 6 TC |
| Энергетический щит | `EnergyShield` | 8 TC |
| Элитный тактический жилет | `ClothingOuterVestWebElite` | 8 TC |
| Зеркальный щит | `MirrorShield` | 9 TC |
| Щит Синего щита | `BlueSheildSheild` | 9 TC |
| Деревянный баклер | `WoodenBuckler` | 1 TC |
| Картонный щит | `CardShield` | 1 TC |
| Самодельный щит | `MakeshiftShield` | 1 TC |
| Паутинный щит | `WebShield` | 1 TC |
| Часовой щит | `ClockworkShield` | 3 TC |
| Сломанный энергощит | `BrokenEnergyShield` | 1 TC |
| Сломанный щит СЩ | `BrokenBlueSheildSheild` | 1 TC |
| Парирующий баклер | `ArenaParryBuckler` | 11 TC |

---

## Броня (лимит 2 шт. за открытие)

### Обычная броня и одежда с бронёй

| Предмет | Entity | Цена |
|---|---|---|
| Скафандр инфильтратора (маскировка) | `ClothingOuterHardsuitInfiltration` | 14 TC |
| Зачарованный МОД-костюм (полный комплект) | `ClothingModularControllerEnchanted` | 16 TC |
| H.E.C.K. костюм (−70% дроб/реж, взрыв/огонь) | `ClothingOuterArmorHostileEnv` | 16 TC |
| Базовая броня | `ClothingOuterArmorBasic` | 2 TC |
| Мусорная броня | `ClothingOuterArmorScrap` | 4 TC |
| Противоударная броня | `ClothingOuterArmorRiot` | 3 TC |
| Отражающая броня | `ClothingOuterArmorReflective` | 4 TC |
| Пулестойкая броня | `ClothingOuterArmorBulletproof` | 5 TC |
| Культистская броня | `ClothingOuterArmorCult` | 7 TC |
| Рейдовая броня | `ClothingOuterArmorRaid` | 6 TC |
| Тяжёлая броня | `ClothingOuterArmorHeavy` | 18 TC |
| Тяжёлая броня (зелёная) | `ClothingOuterArmorHeavyGreen` | 18 TC |
| Тяжёлая броня (красная) | `ClothingOuterArmorHeavyRed` | 18 TC |
| Хитиновый панцирь | `ClothingOuterArmorChangeling` | 4 TC |
| Набор рейдерского костюма Синдиката | `ClothingBackpackSyndicateRaidBundle` | 8 TC |
| Костяная броня | `ClothingOuterArmorBone` | 12 TC |
| Латунная кираса | `VeilCultBrassCuirassArsenal` | 8 TC |
| Броня Pod Wars | `ClothingOuterArmorPodWars` | 8 TC |
| Жилет детектива | `ClothingOuterVestDetective` | 2 TC |
| Тренч детектива | `ClothingOuterCoatDetective` | 3 TC |
| Тренч детектива (стандартный) | `ClothingOuterCoatDetectiveLoadout` | 3 TC |
| Тренч детектива (нуар) | `ClothingOuterCoatDetectiveLoadoutGrey` | 3 TC |
| Тренч детектива (тёмный) | `ClothingOuterCoatDetectiveDark` | 3 TC |
| Тренч ГСБ | `ClothingOuterCoatHoSTrench` | 4 TC |
| Зимняя куртка ГСБ | `ClothingOuterWinterHoS` | 4 TC |
| Бронированное пальто ГСБ | `ClothingOuterCoatHoSGreatcoat` | 5 TC |
| Торжественная куртка капитана | `ClothingOuterCoatCaptain` | 4 TC |
| Куртка главы персонала | `ClothingOuterCoatHOP` | 2 TC |
| Бронированное пальто синдиката | `ClothingOuterCoatSyndieCapArmored` | 4 TC |
| Зимнее бронепальто синдиката | `ClothingOuterWinterSyndieCapArmored` | 4 TC |
| Зимнее пальто Синий Щит | `ClothingOuterWinterBlueShield` | 4 TC |
| Зимнее бронепальто Синий Щит | `ClothingOuterWinterBlueShieldAlt` | 4 TC |
| Куртка смотрителя | `ClothingOuterCoatWarden` | 6 TC |
| Куртка смотрителя (тёмно-синяя) | `ClothingOuterCoatWardenAlt` | 6 TC |
| Зимняя бронекуртка пилота | `ClothingOuterWinterPilot` | 3 TC |
| Шинель службы безопасности | `ClothingOuterCoatSecurityOvercoat` | 3 TC |
| Тактический жилет | `ClothingOuterVestWeb` | 6 TC |
| Элитный тактический жилет | `ClothingOuterVestWebElite` | 8 TC |
| Адреналиновый жилет | `ArenaAdrenalineVest` | 10 TC |
| Адаптивная броня | `ArenaAdaptiveArmor` | 16 TC |
| Тактический жилет наёмника | `ClothingOuterVestWebMerc` | 3 TC |
| Тренч | `ClothingOuterCoatTrench` | 1 TC |
| Доги | `ClothingOuterDogi` | 2 TC |
| Бронированный медицинский халат | `ClothingOuterCoatAMG` | 2 TC |
| Одеяние флаггелянта (не под лимитом брони) | `ClothingOuterFlagellantRobeArsenal` | 2 TC |
| Тонкий базовый бронежилет | `ClothingOuterArmorBasicSlim` | 3 TC |
| Броня Синего щита | `ClothingOuterArmorBlueShield` | 5 TC |
| Карапас капитана | `ClothingOuterArmorCaptainCarapace` | 5 TC |
| Белый карапас капитана | `ClothingOuterArmorCaptainCarapaceWhite` | 5 TC |
| Карапас шерифа | `ClothingOuterArmorSherifCarapace` | 5 TC |
| Броня тамплиера | `ClothingOuterArmorTemplar` | 5 TC |
| Карапас ЦентКом | `ClothingOuterArmorCentcommCarapace` | 6 TC |
| Исследовательский костюм | `ClothingOuterArmorExplorerSuit` | 4 TC |
| Медицинский исследовательский костюм | `ClothingOuterArmorExplorerSuitMedical` | 5 TC |
| Усиленный исследовательский костюм | `ClothingOuterArmorExplorerSuitReinforced` | 7 TC |
| Латунная кираса (с выдвижным шлемом) | `VeilCultBrassCuirassArena` | 6 TC |
| Латунная кираса (Дым) | `VeilCultBrassCuirassSmokeArena` | 8 TC |
| Латунная кираса (Поглощение) | `VeilCultBrassCuirassAbsorbArena` | 8 TC |
| Латунная кираса (Отражение) | `VeilCultBrassCuirassReflectionArena` | 8 TC |
| Латунная кираса (Укрепление) | `VeilCultBrassCuirassHardenPlatesArena` | 8 TC |
| Латунная роба | `ClothingOuterVeilCultRobesArena` | 5 TC |
| Латунная роба (Спешка) | `ClothingOuterVeilCultRobesHasteArena` | 7 TC |
| Латунная роба (Маскировка) | `ClothingOuterVeilCultRobesCamouflageArena` | 7 TC |

### Скафандры

| Предмет | Entity | Цена |
|---|---|---|
| EVA | `ClothingOuterHardsuitEVA` | 3 TC |
| Инженерный | `ClothingOuterHardsuitEngineering` | 4 TC |
| Синдикат (базовый) | `ClothingOuterHardsuitSyndie` | 7 TC |
| ERT Уборщик | `ClothingOuterHardsuitERTJanitor` | 5 TC |
| Инженерный (белый) | `ClothingOuterHardsuitEngineeringWhite` | 6 TC |
| Атмосферный | `ClothingOuterHardsuitAtmos` | 6 TC |
| Пиратский капитан | `ClothingOuterHardsuitPirateCap` | 8 TC |
| Медицинский | `ClothingOuterHardsuitMedical` | 8 TC |
| Охранника | `ClothingOuterHardsuitSecurity` | 8 TC |
| Охранника (красный) | `ClothingOuterHardsuitSecurityRed` | 8 TC |
| Надзирателя | `ClothingOuterHardsuitWarden` | 8 TC |
| Бригмедик | `ClothingOuterHardsuitBrigmedic` | 8 TC |
| Пустотный парамедик | `ClothingOuterHardsuitVoidParamed` | 8 TC |
| Древний EVA | `ClothingOuterHardsuitAncientEVA` | 8 TC |
| Синдикат Медик | `ClothingOuterHardsuitSyndieMedic` | 7 TC |
| Преобразователь кровавого-красного скафандра | `HardsuitOperSelector` | 7 TC |
| Преобразователь медицинского кровавого-красного скафандра | `HardsuitOperMedSelector` | 7 TC |
| Синдикат Элита | `ClothingOuterHardsuitSyndieElite` | 12 TC |
| Джаггернаут | `ClothingOuterHardsuitJuggernaut` | 10 TC |
| Пилот охраны | `ClothingOuterHardsuitSecurityPilot` | 10 TC |
| НД | `ClothingOuterHardsuitRd` | 10 TC |
| Максим | `ClothingOuterHardsuitMaxim` | 10 TC |
| Волшебник | `ClothingOuterHardsuitWizard` | 10 TC |
| ERT Инженер | `ClothingOuterHardsuitERTEngineer` | 10 TC |
| ERT Медик | `ClothingOuterHardsuitERTMedical` | 10 TC |
| ERT Капеллан | `ClothingOuterHardsuitERTChaplain` | 10 TC |
| Капитанский | `ClothingOuterHardsuitCap` | 12 TC |
| ERT Охрана | `ClothingOuterHardsuitERTSecurity` | 12 TC |
| ERT Лидер | `ClothingOuterHardsuitERTLeader` | 15 TC |
| Синдикат Командир | `ClothingOuterHardsuitSyndieCommander` | 15 TC |
| CBURN | `ClothingOuterHardsuitCBURN` | 15 TC |
| CBURN Лидер | `ClothingOuterHardsuitCBURNLeader` | 18 TC |
| Отряд смерти | `ClothingOuterHardsuitDeathsquad` | 35 TC (80 TC в Мега-ящике 120 ТК) |
| Скафандр космонавта | `ClothingOuterHardsuitSpatio` | 7 TC |
| Скафандр Голиафа | `ClothingOuterHardsuitGoliath` | 8 TC |
| Скафандр сальвейджа | `ClothingOuterHardsuitSalvage` | 8 TC |
| Скафандр Синего щита | `ClothingOuterHardsuitBlueShield` | 10 TC |
| Скафандр Пустоты | `ClothingOuterHardsuitVoid` | 10 TC |
| Улучшенный скафандр Пустоты | `ClothingOuterHardsuitVoidAdvanced` | 12 TC |
| Силовая броня космодесанта | `ClothingOuterHardsuitSpaceMarine` | 25 TC |

### МОД-скафандры (предсобранные)

| Предмет | Entity | Цена |
|---|---|---|
| МОД Стандартный | `ClothingModularControllerStandardPreassembled` | 3 TC |
| МОД Гражданский | `ClothingModularControllerCivilianPreassembled` | 3 TC |
| МОД Космохонк | `ClothingModularControllerCosmohonkPreassembled` | 3 TC |
| МОД Погрузочный | `ClothingModularControllerLoaderPreassembled` | 4 TC |
| МОД Прототип | `ClothingModularControllerPrototypePreassembled` | 4 TC |
| МОД Шахтёрский | `ClothingModularControllerMiningPreassembled` | 4 TC |
| МОД Атмосферный | `ClothingModularControllerAtmosphericPreassembled` | 6 TC |
| МОД Астероидный | `ClothingModularControllerAsteroidPreassembled` | 6 TC |
| МОД Научный | `ClothingModularControllerResearchPreassembled` | 6 TC |
| МОД Синдикат | `ClothingModularControllerSyndicatePreassembled` | 7 TC |
| МОД Инженерный | `ClothingModularControllerEngineeringPreassembled` | 8 TC |
| МОД Продвинутый | `ClothingModularControllerAdvancedPreassembled` | 8 TC |
| МОД Магнатский | `ClothingModularControllerMagnatePreassembled` | 8 TC |
| МОД Охрана | `ClothingModularControllerSecurityPreassembled` | 10 TC |
| МОД Медицинский | `ClothingModularControllerMedicalPreassembled` | 10 TC |
| МОД Спасательный | `ClothingModularControllerRescuePreassembled` | 11 TC |
| МОД Элита | `ClothingModularControllerElitePreassembled` | 12 TC |
| МОД Страж | `ClothingModularControllerSafeguardPreassembled` | 12 TC |
| МОД Ответный (ОБР) | `ClothingModularControllerResponsoryPreassembled` | 12 TC |
| МОД Инквизиторский | `ClothingModularControllerInquisitoryPreassembled` | 12 TC |
| МОД Корпоративный | `ClothingModularControllerCorporatePreassembled` | 14 TC |
| МОД Апокрифический | `ClothingModularControllerApocryphalPreassembled` | 35 TC (80 TC в Мега-ящике 120 ТК) |

---

## Перчатки и кастеты

| Предмет | Entity | Цена |
|---|---|---|
| Кастет | `ClothingHandsKnuckleDusters` | 1 TC |
| Перчатки наёмника без пальцев | `ClothingHandsGlovesMercFingerless` | 1 TC |
| Боевые перчатки | `ClothingHandsGlovesCombat` | 2 TC |
| Боевые перчатки наёмника | `ClothingHandsMercGlovesCombat` | 2 TC |
| Латунный кастет | `ClothingHandsKnuckleDustersBrass` | 2 TC |
| Кастет квартирмейстера | `ClothingHandsKnuckleDustersQM` | 2 TC |
| Боевые перчатки Синего щита | `ClothingHandsGlovesCombatBlueShield` | 3 TC |
| Силовая перчатка | `ClothingHandsGlovesPowerglove` | 3 TC |
| Дробители костей | `ClothingHandsKnuckleBoneCrushers` | 3 TC |
| Накачанные боксёрские перчатки (синие) | `ClothingHandsGlovesBoxingRiggedBlue` | 4 TC |
| Накачанные боксёрские перчатки (зелёные) | `ClothingHandsGlovesBoxingRiggedGreen` | 4 TC |
| Накачанные боксёрские перчатки (красные) | `ClothingHandsGlovesBoxingRiggedRed` | 4 TC |
| Накачанные боксёрские перчатки (жёлтые) | `ClothingHandsGlovesBoxingRiggedYellow` | 4 TC |
| Перчатки космического ниндзя | `ClothingHandsGlovesSpaceNinja` | 4 TC |
| Кастет Синдиката | `ClothingHandsKnuckleDustersSyndicate` | 4 TC |
| Контузионные перчатки | `ClothingHandsGlovesConcussiveGauntlets` | 5 TC |
| Латунные латные перчатки | `ClothingHandsClockworkGauntletsArsenal` | 6 TC |
| Перчатки Крав-мага | `ClothingHandsGlovesKravMaga` | 5 TC |
| Перчатки Полярной звезды | `ClothingHandsGlovesNorthStar` | 8 TC |
| Перчатки алой жатвы корниворы | `CornivoreGloves` | 12 TC |
| Латунные перчатки | `ClothingHandsClockworkGauntletsArena` | 3 TC |
| Латунные перчатки (Полярная звезда) | `ClothingHandsClockworkGauntletsNorthStarArena` | 5 TC |
| Латунные перчатки (Красное пламя) | `ClothingHandsClockworkGauntletsRedFlameArena` | 5 TC |
| Латунные перчатки (Оглушение) | `ClothingHandsClockworkGauntletsStunArena` | 5 TC |

---

## Магнитные ботинки

| Предмет | Entity | Цена |
|---|---|---|
| Магнитные ботинки | `ClothingShoesBootsMag` | 2 TC |
| Кроваво-красные магнитные ботинки | `ClothingShoesBootsMagSyndie` | 2 TC |
| Боевые магнитные ботинки | `ClothingShoesBootsMagCombat` | 3 TC |
| Улучшенные магнитные ботинки | `ClothingShoesBootsMagAdv` | 5 TC |
| Улучшенные кроваво-красные магнитные ботинки | `ClothingShoesBootsMagAdvSynd` | 8 TC |
| Заводные сапоги (немагнитные) | `ClothingShoesClockworkTreads` | 2 TC |
| Молниеносные магнитные ботинки | `ClothingShoesBootsMagBlinding` | 8 TC |
| Ботинки космического ниндзя | `ClothingShoesSpaceNinja` | 20 TC |

---

## Медицина

| Предмет | Entity | Цена |
|---|---|---|
| Пластырь от ушибов | `Brutepack` | 1 TC |
| Мазь | `Ointment` | 1 TC |
| Мазь алоэ | `AloeCream` | 1 TC |
| Медицинские швы | `MedicatedSuture` | 1 TC |
| Бинт | `Gauze` | 1 TC |
| Пакет крови | `Bloodpack` | 1 TC |
| Регенеративная сетка | `RegenerativeMesh` | 2 TC |
| Аптечка (ушибы) | `MedkitBruteFilled` | 2 TC |
| Аптечка (ожоги) | `MedkitBurnFilled` | 2 TC |
| Аптечка (яды) | `MedkitToxinFilled` | 2 TC |
| Аптечка (кислород) | `MedkitOxygenFilled` | 2 TC |
| Боевая аптечка | `MedkitCombatFilled` | 5 TC |
| Продвинутая аптечка | `MedkitAdvancedFilled` | 5 TC |
| Синдикатский дефибриллятор | `DefibrillatorSyndicate` | 12 TC |
| Кремовый банановый пирог | `FoodPieBananaCream` | 5 TC |
| Бикаридин (бутылка) | `ChemistryBottleBicaridine` | 2 TC |
| Дермалин (бутылка) | `ChemistryBottleDermaline` | 2 TC |
| Диловен (бутылка) | `ChemistryBottleDylovene` | 1 TC |
| Эпинефрин (бутылка) | `ChemistryBottleEpinephrine` | 3 TC |
| Келотан (бутылка) | `ChemistryBottleKelotane` | 1 TC |
| Транексамовая кислота (бутылка) | `ChemistryBottleTranexamicAcid` | 2 TC |
| Трикордразин (бутылка) | `ChemistryBottleTricordrazine` | 2 TC |
| Омнизин (бутылка) | `ChemistryBottleOmnizine` | 4 TC |
| Эфедрин (бутылка) | `ChemistryBottleEphedrine` | 3 TC |
| Физраствор (бутылка) | `ChemistryBottleSaline` | 1 TC |
| Пунктураза (бутылка) | `ChemistryBottlePuncturase` | 2 TC |
| Бруизин (бутылка) | `ChemistryBottleBruizine` | 2 TC |
| Лацеринол (бутылка) | `ChemistryBottleLacerinol` | 2 TC |
| Пиразин (бутылка) | `ChemistryBottlePyrazine` | 2 TC |
| Синаптизин (бутылка) | `ChemistryBottleSynaptizine` | 2 TC |
| Зажигательная смесь напалм+флогистон (компаньон к водяному оружию) | `ChemistryBottleNapalmPhlogiston` | 5 TC |
| Автоинъектор от травм | `BruteAutoInjector` | 1 TC |
| Автоинъектор от ожогов | `BurnAutoInjector` | 1 TC |
| Автоинъектор от проколов | `PunctAutoInjector` | 1 TC |
| Автоинъектор от температуры | `PyraAutoInjector` | 1 TC |
| Коробка автоинъекторов (полный набор) | `BoxAutoInjectorAll` | 3 TC |
| Экстренный медипен | `EmergencyMedipen` | 2 TC |
| Медипен выживания | `SurvivalMedipen` | 2 TC |
| Боевой медипен | `CombatMedipen` | 4 TC |

---

## Лечащая еда

Восстанавливает здоровье за счёт реагентов в составе (Omnizine — лечит всё, Bicaridine — ушибы, Kelotane/Dermaline — ожоги, Ichor — драконьи блюда, лечит всё + кровопотерю).

| Предмет | Entity | Цена |
|---|---|---|
| Амброзия деус | `FoodAmbrosiaDeus` | 2 TC |
| Амброзия вульгарис | `FoodAmbrosiaVulgaris` | 1 TC |
| Алоэ | `FoodAloe` | 1 TC |
| Мак | `FoodPoppy` | 1 TC |
| Лилия | `FoodLily` | 1 TC |
| Божественный салат | `FoodSaladAesir` | 3 TC |
| Травяной салат | `FoodSaladHerb` | 2 TC |
| Эдемский салат | `FoodSaladEden` | 2 TC |
| Крапивный суп | `FoodSoupNettle` | 1 TC |
| Тёплый хонк-покет | `FoodDonkpocketHonkWarm` | 1 TC |
| Тёплый ягодный покет | `FoodDonkpocketBerryWarm` | 1 TC |
| Тёплый моль-покет | `FoodDonkpocketMothWarm` | 1 TC |
| Креветка | `Shrimp` | 1 TC |
| Плод кактуса-бочонка | `FoodFloraCactusFruit` | 1 TC |
| Драконья плоть | `FoodMeatDragon` | 2 TC |
| Драконий стейк | `FoodMeatDragonCooked` | 2 TC |
| Сырая драконья вырезка | `FoodMeatDragonCutlet` | 1 TC |
| Драконья вырезка | `FoodMeatDragonCutletCooked` | 1 TC |
| Драко (тако) | `FoodTacoDragon` | 1 TC |

---

## Утилита

| Предмет | Entity | Цена |
|---|---|---|
| Нескользящие ботинки | `ClothingShoesChameleonNoSlips` | 2 TC |
| Наручники | `Handcuffs` | 1 TC |
| Фонарик | `FlashlightSeclite` | 1 TC |
| Солнцезащитные очки | `ClothingEyesGlassesSunglasses` | 2 TC |
| Очки наёмника | `ClothingEyesGlassesMercenary` | 2 TC |
| Визор ниндзя | `ClothingEyesVisorNinja` | 1 TC |
| Нуар-очки | `ClothingEyesGlassesNoir` | 1 TC |
| Тепловизор | `ClothingEyesGlassesThermal` | 3 TC |
| Тепловизионные очки снайпера | `ClothingEyesGlassesSniperThermalSyndie` | 10 TC |
| Очки охраны | `ClothingEyesGlassesSecurity` | 2 TC |
| Мед-охранные очки | `ClothingEyesGlassesMedSecSunglasses` | 2 TC |
| Медицинский визор | `ClothingEyesHudMedical` | 1 TC |
| Визор охраны | `ClothingEyesHudSecurity` | 1 TC |
| Мед-охранный визор | `ClothingEyesHudMedSec` | 2 TC |
| Медицинский моновизор | `ClothingEyesEyepatchHudMedical` | 1 TC |
| Моновизор охраны | `ClothingEyesEyepatchHudSecurity` | 1 TC |
| Дымовая граната | `SmokeGrenade` | 1 TC |
| Флэш | `Flash` | 1 TC |
| Туристическая камера | `TravelCamera` | 2 TC |
| Омега-мыло | `SoapOmega` | 2 TC |
| Одноразовая баллистическая турель | `ToolboxElectricalTurretFilled` | 4 TC |
| Кабельные стяжки | `Cablecuffs` | 1 TC |
| Стяжки | `Zipties` | 1 TC |
| Коробка наручников | `BoxHandcuff` | 2 TC |
| Коробка стяжек | `BoxZiptie` | 2 TC |
| Переносной зарядник | `PortableRecharger` | 2 TC |

---

## Гранаты (лимит 3 за открытие)

| Предмет | Entity | Цена |
|---|---|---|
| Светошумовая | `GrenadeFlashBang` | 1 TC |
| Слезоточивый газ | `TearGasGrenade` | 1 TC |
| ЭМИ | `EmpGrenade` | 2 TC |
| Дубинка-граната | `GrenadeBaton` | 2 TC |
| Клинада | `GrenadeCleanade` | 1 TC |
| Осколочная | `ExGrenade` | 3 TC |
| Шрапнельная | `GrenadeShrapnel` | 3 TC |
| Зажигательная | `GrenadeIncendiary` | 3 TC |
| Кассетная светошумовая | `ClusterBang` | 3 TC |
| Святая ручная граната | `HolyHandGrenade` | 3 TC |
| Кассетная | `ClusterGrenade` | 4 TC |
| Кассетная светошумовая (полная) | `ClusterBangFull` | 4 TC |
| Мини-бомба синдиката | `SyndieMiniBomb` | 4 TC |
| Хитрая бомба синдиката | `SyndieTrickyBomb` | 5 TC |
| Граната-жалюзи | `GrenadeStinger` | 2 TC |

---

## Импланты

| Предмет | Entity | Цена |
|---|---|---|
| Имплант свободы | `FreedomImplanter` | 2 TC |
| Имплант SCRAM | `ScramImplanter` | 2 TC |
| ЭМИ-имплант | `EmpImplanter` | 2 TC |
| Микробомба | `MicroBombImplanter` | 4 TC |
| Хранилище (имплант) | `StorageImplanter` | 6 TC |

---

## Гаджеты и инструменты

| Предмет | Entity | Цена |
|---|---|---|
| Синдикатские челюсти жизни | `SyndicateJawsOfLife` | 2 TC |
| Стелс-коробка | `StealthBox` | 5 TC |
| Гипоручка (коробка) | `HypopenBox` | 6 TC |
| Взрывающаяся ручка (коробка) | `PenExplodingBox` | 1 TC |
| Гипо-дарт (коробка) | `HypoDartBox` | 1 TC |
| Гипоспрей | `Hypospray` | 2 TC |
| Горлекс-гипоспрей | `SyndiHypo` | 2 TC |
| Гипоручка | `Hypopen` | 2 TC |
| Ручка Киберсан | `CyberPen` | 1 TC |
| Стимпак (гиперзин, полный) | `Stimpack` | 4 TC |
| Микроинъектор гиперзина | `StimpackMini` | 1 TC |
| Вех-автошприц | `WehMedipen` | 1 TC |
| Настоящий молочный коктейль (30 ед.) | `DrinkTrueMilkshakeFilled` | 5 TC |

---

## Ящики с патронами (по 1 TC каждый)

| Предмет | Entity |
|---|---|
| Пистолетные патроны | `MagazineBoxPistol` |
| Лёгкие винтовочные патроны | `MagazineBoxLightRifle` |
| Винтовочные патроны | `MagazineBoxRifle` |
| Магнум патроны | `MagazineBoxMagnum` |
| Безгильзовые патроны | `MagazineBoxCaselessRifle` |
| Магазин SIAR-52 удлинённый (безгильзовый, 35 зар.) | `MagazinePistolSubMachineGunCaselessExtended` | 2 TC |
| Противоматериальные патроны | `MagazineBoxAntiMateriel` |
| Дробовые патроны (стандарт) | `MagazineBoxShotgun` |
| Дробовые патроны (коробка) | `BoxMagazineShotgun` |

---

## Спецпатроны (по 2 TC каждый)

| Предмет | Entity |
|---|---|
| Магазин пистолетный зажигательный | `MagazinePistolIncendiary` |
| Магазин пистолетный урановый | `MagazinePistolUranium` |
| Магазин ПП зажигательный | `MagazinePistolSubMachineGunIncendiary` |
| Магазин ПП урановый | `MagazinePistolSubMachineGunUranium` |
| Магазин винтовочный зажигательный | `MagazineRifleIncendiary` |
| Магазин винтовочный урановый | `MagazineRifleUranium` |
| Магазин лёгкой винтовки зажигательный | `MagazineLightRifleIncendiary` |
| Магазин лёгкой винтовки урановый | `MagazineLightRifleUranium` |
| Обойма магнум бронебойная | `SpeedLoaderMagnumAP` |
| Обойма магнум зажигательная | `SpeedLoaderMagnumIncendiary` |
| Обойма магнум урановая | `SpeedLoaderMagnumUranium` |
| Магазин Desert Eagle ББ | `MagazineEagleAP` |
| Магазин дробовика (пули) | `MagazineShotgunSlug` |
| Магазин дробовика (резина) | `MagazineShotgunBeanbag` |
| Магазин дробовика (зажигательный) | `MagazineShotgunIncendiary` |

---

## Оружейные бандли (чемоданы)

| Предмет | Entity | Цена |
|---|---|---|
| Снайперский набор | `BriefcaseSyndieSniperBundleFilled` | 6 TC |
| Чемодан-автомат (C-20K) | `WeaponSubMachineGunBriefcase` | 10 TC |
| Набор лоббиста | `BriefcaseSyndieLobbyingBundleFilled` | 2 TC |
| Набор метательных ножей | `ThrowingKnivesKit` | 3 TC |
| Багетный набор | `CombatBakeryKit` | 4 TC |

---

## Оружейные бандли (вещмешки)

| Предмет | Entity | Цена |
|---|---|---|
| Вещмешок винтовка | `ClothingBackpackDuffelSyndicateFilledRifle` | 8 TC |
| Вещмешок револьвер | `ClothingBackpackDuffelSyndicateFilledRevolverStandard` | 8 TC |
| Вещмешок патроны | `ClothingBackpackDuffelSyndicateAmmoFilled` | 10 TC |
| Вещмешок Hushpup | `ClothingBackpackDuffelSyndicateFilledHushpup` | 10 TC |
| Вещмешок ПП (SMG) | `ClothingBackpackDuffelSyndicateFilledSMG` | 13 TC |
| Вещмешок карабин | `ClothingBackpackDuffelSyndicateFilledCarbine` | 15 TC |
| Вещмешок Hristov МК2 | `ClothingBackpackDuffelSyndicateFilledHristov` | 18 TC |
| Вещмешок XC67 | `ClothingBackpackDuffelSyndicateFilledXC67` | 19 TC |
| Вещмешок дробовик | `ClothingBackpackDuffelSyndicateFilledShotgun` | 20 TC |
| Вещмешок гранатомёт | `ClothingBackpackDuffelSyndicateFilledGrenadeLauncher` | 25 TC |
| Вещмешок пулемёт LMG | `ClothingBackpackDuffelSyndicateFilledLMG` | 30 TC |
| Вещмешок скафандр Синдиката | `ClothingBackpackDuffelSyndicateHardsuitBundle` | 8 TC |
| Вещмешок элитный скафандр | `ClothingBackpackDuffelSyndicateEliteHardsuitBundle` | 13 TC |
| Армейская РПС | `ClothingBeltMilitaryWebbing` | 2 TC |
| РПС шахтёра | `ClothingBeltSalvageWebbing` | 2 TC |
| РПС охраны | `ClothingBeltSecurityWebbing` | 2 TC |
| Вещмешок костюм клоуна | `ClothingBackpackDuffelSyndicateCostumeClown` | 1 TC |
| ЕКД вещмешок Синдиката | `ClothingBackpackDuffelSyndicateEVABundle` | 3 TC |
| Вещмешок M-90gl | `ClothingBackpackDuffelArenaM90gl` | 10 TC |
| Вещмешок ASH-12 | `ArenaDuffelAsh12Bundle` | 20 TC |
| Вещмешок AS-12 «Минотавр» | `ArenaDuffelMinotaurBundle` | 20 TC |
| Комплект «Маг любви» (подарок: гримуар арены, броня эскадрона смерти, AS-12, импульсный пистолет, ПП «Донксофт», форма горничной, 2×наручники, шибари, шипастая дубинка) | `ArenaLoveMageSet` | 120 TC |
| Вещмешок P90 | `ArenaDuffelP90Bundle` | 30 TC |
| Набор лучника | `ArenaDuffelBowSet` | 4 TC |
| Ядовитый набор лучника | `ArenaDuffelBowSetPoison` | 8 TC |
| Дестабилизирующий набор лучника | `ArenaDuffelBowSetNocturine` | 8 TC |
| Набор купидона | `ArenaDuffelBowSetAphrodisiac` | 8 TC |
| Элитный набор лучника | `ArenaDuffelBowSetElite` | 8 TC |
| Кубик лучника | `ArenaBowLuckDie` | 120 TC |
| Воин коммунизма | `ArenaCommunismSet` | 22 TC |
| Набор гладиатора | `ArenaGladiatorSet` | 8 TC |
| Набор самурая | `ArenaSamuraiSet` | 5 TC |
| Набор стрелка | `ArenaWestSet` | 7 TC |
| Набор капиталиста | `ArenaCapitalistSet` | 5 TC |
| Чумба, ты совсем ё@%&тый? | `ArenaNorthStarSet` | 120 TC |
| Чумба, ты совсем ё@%&тый? (имплант) | `ArenaNorthStarImplantSet` | 120 TC |
| Набор святоши | `ArenaHolyManSet` | 6 TC |
| Травматическая аптечка | `ArenaSurvivalTraumaKit` | 6 TC |
| Противоожоговая аптечка | `ArenaSurvivalBurnKit` | 6 TC |
| Детокс-аптечка | `ArenaSurvivalDetoxKit` | 6 TC |
| Набор берсерка | `ArenaSurvivalBerserkerKit` | 9 TC |
| Блиц-набор | `ArenaSurvivalBlitzKit` | 10 TC |
| Набор полевого медика | `ArenaSurvivalFieldKit` | 7 TC |
| Перевязочный набор | `ArenaSurvivalDressingKit` | 4 TC |

---

## Пустые сумки и рюкзаки (лимит 1 за открытие — `FullArsenalBag`, все по 1 TC)

| Предмет | Entity | Цена |
|---|---|---|
| Рюкзак Синдиката | `ClothingBackpackSyndicate` | 1 TC |
| Вещмешок Синдиката | `ClothingBackpackDuffelSyndicate` | 1 TC |
| Рюкзак охраны | `ClothingBackpackSecurity` | 1 TC |
| Рюкзак наёмника | `ClothingBackpackMerc` | 1 TC |
| Рюкзак | `ClothingBackpack` | 1 TC |
| Кожаная сумка | `ClothingBackpackSatchelLeather` | 1 TC |
| Сумка | `ClothingBackpackSatchel` | 1 TC |
| Вещмешок | `ClothingBackpackDuffel` | 1 TC |

Рюкзаки ОБР / эскадрона смерти (огромная вместимость + взрывозащита −90%, тоже `FullArsenalBag`, лимит 1):

| Предмет | Entity | Цена |
|---|---|---|
| Рюкзак ОБР (лидер) | `ClothingBackpackERTLeader` | 3 TC |
| Рюкзак ОБР (охрана) | `ClothingBackpackERTSecurity` | 3 TC |
| Рюкзак ОБР (медик) | `ClothingBackpackERTMedical` | 3 TC |
| Рюкзак ОБР (инженер) | `ClothingBackpackERTEngineer` | 3 TC |
| Рюкзак ОБР (уборщик) | `ClothingBackpackERTJanitor` | 3 TC |
| Рюкзак ОБР (клоун) | `ClothingBackpackERTClown` | 3 TC |
| Рюкзак ОБР (священник) | `ClothingBackpackERTChaplain` | 3 TC |
| Рюкзак эскадрона смерти | `ClothingBackpackDeathSquad` | 4 TC |

Не считаются «сумкой» (отдельный слот, под лимит не идут):
- Кожаная поясная сумка | `ClothingBeltStorageWaistbag` | 1 TC — слот пояса.
- Скороходы (с заряженной малой батареей) | `ClothingShoesBootsSpeedFilled` | 4 TC — **только в FullArsenal-ящиках**, не в мелейном. Батарею можно вынуть, на ходу разряжается.

---

## Шлемы (лимит 1 за открытие)

| Предмет | Entity | Цена |
|---|---|---|
| Бронированная рабочая каска | `ClothingHeadHatHardhatArmored` | 2 TC |
| H.E.C.K. шлем (броня, защита глаз, иммунитет к вспышкам) | `ClothingHeadHelmetHostileEnv` | 5 TC |
| Базовый шлем | `ClothingHeadHelmetBasic` | 1 TC |
| Мусорный шлем | `ClothingHeadHelmetScrap` | 1 TC |
| Противоударный шлем | `ClothingHeadHelmetRiot` | 2 TC |
| Флэш-шлем | `ClothingHeadHelmetFlash` | 2 TC |
| Шлем наёмника | `ClothingHeadHelmetMerc` | 2 TC |
| Костяной шлем | `ClothingHeadHelmetBone` | 2 TC |
| Шлем ERT Уборщика | `ClothingHeadHelmetERTJanitor` | 2 TC |
| Культистский шлем | `ClothingHeadHelmetCult` | 3 TC |
| Шлем синдиката | `ClothingHeadHelmetSyndicate` | 3 TC |
| Шлем SWAT | `ClothingHeadHelmetSwat` | 3 TC |
| Шлем SWAT Синдиката | `ClothingHeadHelmetSwatSyndicate` | 3 TC |
| Рейдовый шлем | `ClothingHeadHelmetRaid` | 3 TC |
| Шлем Pod Wars | `ClothingHeadHelmetPodWars` | 3 TC |
| Шлем медика охраны | `ClothingHeadHelmetSecurityMedic` | 3 TC |
| Шлем Громовой арены | `ClothingHeadHelmetThunderdome` | 3 TC |
| Шлем пустотного парамедика | `ClothingHeadHelmetVoidParamed` | 4 TC |
| Шлем тамплиера | `ClothingHeadHelmetTemplar` | 4 TC |
| Шлем Космического ниндзя | `ClothingHeadHelmetSpaceNinja` | 4 TC |
| Шлем ERT Инженера | `ClothingHeadHelmetERTEngineer` | 4 TC |
| Шлем ERT Медика | `ClothingHeadHelmetERTMedic` | 4 TC |
| Шлем ERT Охраны | `ClothingHeadHelmetERTSecurity` | 4 TC |
| Древний шлем | `ClothingHeadHelmetAncient` | 5 TC |
| Шлем ERT Лидера | `ClothingHeadHelmetERTLeader` | 5 TC |

---

## Противогазы (лимит 1 за открытие, общий с шлемами)

| Предмет | Entity | Цена |
|---|---|---|
| Противогаз | `ClothingMaskGas` | 1 TC |
| Противогаз атмосферника | `ClothingMaskGasAtmos` | 2 TC |
| Противогаз охраны | `ClothingMaskGasSecurity` | 2 TC |
| Противогаз наёмника | `ClothingMaskGasMerc` | 2 TC |
| Противогаз SWAT | `ClothingMaskGasSwat` | 2 TC |
| Противогаз синдиката | `ClothingMaskGasSyndicate` | 2 TC |
| Противогаз ERT | `ClothingMaskGasERT` | 3 TC |
| Противогаз Отряда смерти | `ClothingMaskGasDeathSquad` | 4 TC |

---

## Безделушки

| Предмет | Entity | Цена |
|---|---|---|
| Арбузная шляпа | `ClothingHeadHatWatermelon` | 1 TC |
| Синдикатское мыло | `SoapSyndie` | 1 TC |
| Кластерное мыло Синдиката | `SlipocalypseClusterSoap` | 3 TC |
| Скользкий тип | `ArenaDuffelSlipperyBundle` | 10 TC |
| Набор курильщика Интердайн | `InterdyneSmokerKit` | 2 TC |
| Синдикатский плеер | `SyndiePlayerInstrument` | 1 TC |
| Синдикатский кошелёк | `PouchSyndie` | 1 TC |
| Ящик инструментов Синдиката | `ToolboxSyndicateFilled` | 2 TC |
| Сегвей Синдиката (ящик) | `CrateFunSyndicateSegway` | 2 TC |
| Воздушный шарик Синдиката | `BalloonSyn` | 10 TC |
| Кроличьи ушки | `ClothingHeadHatBunny` | 5 TC |
| Собачьи ушки | `ClothingHeadHatDogEars` | 10 TC |
| Кошачьи ушки | `ClothingHeadHatCatEars` | 26 TC |
| Элегантное платье горничной | `ClothingUniformJumpskirtElegantMaid` | 30 TC |
| Кубик судьбы | `DiceOfFate` | 10 TC |
| Кость войны (боевой d20) | `DiceOfWar` | 10 TC |
| Пистолет-пугач | `RevolverCapGun` | 10 TC |
| Пистолет-пугач (фальшивый) | `RevolverCapGunFake` | 10 TC |
| Игрушечный световой меч | `ToySword` | 8 TC |
| Гей-пак (шарф + значок + плащ) | `BackpackGayPack` | 2 TC |

---

## Книги заклинаний

| Предмет | Entity | Цена |
|---|---|---|
| Призыв существ | `SpawnSpellbook` | 10 TC |
| Самовозгорание | `FireSelfSpellbook` | 10 TC |
| Мигание (Blink) | `BlinkBook` | 10 TC |
| Стена силы | `ForceWallSpellbook` | 10 TC |
| Руны | `ScrollRunes` | 10 TC |
