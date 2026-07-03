# TODO — wega-mega

## Катана Рэнгоку (ArenaRengokuKatana)

- [ ] **Нарисовать кастомные кадры эффектов.** Эффекты доработаны компоновкой
  ванильных слоёв (`Effects/explosion.rsi` + `Effects/fire.rsi` + искры
  `Effects/sparks.rsi`) и кодом: Первая форма рисует веер пламени перед
  носителем, Девятая — огненный след вдоль рывка и кольцо взрыва (см.
  `Resources/Prototypes/_Wega/Entities/Effects/rengoku_katana.yml` и
  `RengokuKatanaSystem`). Выглядит уже прилично, но для идеала всё ещё хочется
  собственные нарисованные кадры под огненные приёмы, а не свод ванильных.
- [ ] **Переделать звук удара.** Текущий `Audio/_Wega/Weapons/rengoku_slash.ogg`
  — авто-свод `bladeslice` + `fire` (см. `Tools/gen_rengoku_slash_sound.sh`).
  Хочется нормальный звук пореза+огня, а не миксованный на коленке.
