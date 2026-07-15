# Анонс переключения спавнера снабжения (SpawnerSignalControlSystem).
# { $seconds } подставляется из реального интервала спавнера — число не хардкодится.
spawner-signal-control-enabled = Сброс снаряжения активирован. Ящики будут появляться в центре арены каждые { $seconds } секунд.
spawner-signal-control-disabled = Сброс снаряжения остановлен.

# Анонсы дуэльной арены (DuelArenaSystem)

duel-arena-not-started-no-fighters = Дуэль не началась: в зоне нет бойцов.
duel-arena-not-started-need-two = Дуэль не началась: нужно минимум 2 бойца.

duel-arena-started = Дуэль началась! { $fighters }

duel-arena-scores-reset = Счёт дуэльной арены обнулён.

duel-arena-cleaned = Арена очищена: выданное снаряжение убрано.

duel-arena-losers-fallback = противники

duel-arena-concluded-winner = Дуэль завершена! Победитель: { $winner }{ $streak ->
        [0] { "" }
        [1] { "" }
       *[other] { " " }(побед подряд: { $streak })
    }! { $losers } { $loserCount ->
        [one] потерял сознание
       *[other] потеряли сознание
    }. Снаряжение убрано.

duel-arena-concluded-draw = Ничья! { $fighters } потеряли сознание. Снаряжение убрано.

# Общий накопленный счёт арены, дописывается к итогу боя
duel-arena-scoreboard = Общий счёт: { $scores }

# Ready-check кнопки дуэли (DuelReadySystem) — старт только когда готовы оба бойца
duel-ready-already-active = Бой уже идёт.
duel-ready-fighter-ready = { $name } готов! ({ $count }/{ $total })
duel-ready-fighter-unready = { $name } отменил готовность. ({ $count }/{ $total })

# Шторм (battle-royale): сужающаяся безопасная зона
arena-storm-incoming = Шторм наступает! Безопасная зона сужается — держитесь центра арены.
arena-storm-cancelled = Шторм отменён: зона больше не сужается.

# Соединители для перечисления имён бойцов
duel-arena-connector-vs = против
duel-arena-connector-and = и

# Админ-команда duelscorereset
cmd-duelscorereset-desc = Обнуляет накопленный счёт побед на всех дуэльных и босс-аренах.
cmd-duelscorereset-help = Использование: { $command }
cmd-duelscorereset-invalid-args = Неверные аргументы. Использование: { $command }
cmd-duelscorereset-result = Счёт обнулён на аренах: { $count }.

# Усиление для проигравшего 3 раза подряд
duel-arena-loser-minion-spawned = { $name } получает миньона-помощника после серии поражений!

# Команда arenazone — управление зоной арены (шторм + авиаудары)
cmd-arenazone-desc = Управляет зоной дуэльной арены: включает или выключает шторм и/или авиаудары на ближайшей арене.
cmd-arenazone-help = Использование: { $command } <on|off>
cmd-arenazone-invalid-args = Неверные аргументы. Использование: { $command } <on|off>
cmd-arenazone-no-arena = Не найдено дуэльной арены на текущей карте.
cmd-arenazone-off-result = Зона отключена: шторм — { $storm }, авиаудары — { $airstrike }.
cmd-arenazone-on-result = Зона включена: шторм — { $storm }, авиаудары — { $airstrike }.

# Команда arenaarsenal — задаёт тир арсенал-крейта, спавнящегося у спавн-маркеров каждый раунд
cmd-arenaarsenal-desc = Задаёт тир арсенал-крейта (FullArsenal), спавнящегося у спавн-маркеров каждой арены при старте раунда. Применяется ко всем аренам и полностью заменяет прежний тир.
cmd-arenaarsenal-help = Использование: { $command } <прототип-крейта | off>
cmd-arenaarsenal-invalid-args = Неверные аргументы. Использование: { $command } <прототип-крейта | off>
cmd-arenaarsenal-bad-crate = Прототип «{ $crate }» не найден или не является арсенал-крейтом (нет компонента SurplusBundle).
cmd-arenaarsenal-set-result = Тир арсенала задан: { $crate }. Обновлено арен: { $count }.
cmd-arenaarsenal-off-result = Спавн арсенал-крейтов отключён. Обновлено арен: { $count }.

# Пульт арсенала арены (ArenaArsenalRemoteSystem)
arena-arsenal-remote-title = Пульт арсенала арены
arena-arsenal-remote-info = Выберите тир арсенал-крейтов. Применяется ко всем аренам и действует со следующего раунда.
arena-arsenal-remote-current = { $name } (текущий)
arena-arsenal-remote-off = Отключить спавн крейтов
arena-arsenal-remote-applied = Тир арсенала: { $tier }. Обновлено арен: { $count }.

# Окно кнопки входа на арену (хаб ротации): выбор тира ящиков + кнопка «Войти».
arena-entry-title = Вход на арену
arena-entry-info = Выберите тир арсенал-ящиков и нажмите «Войти». Тир применится ко всем аренам, дуэлянтов перенесёт на арену, ящики появятся у спавнов.
arena-entry-enter = Войти
