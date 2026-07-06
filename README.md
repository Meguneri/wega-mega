<div class="header" align="center">
<img alt="Space Station 14" width="880" height="300" src="https://raw.githubusercontent.com/space-wizards/asset-dump/de329a7898bb716b9d5ba9a0cd07f38e61f1ed05/github-logo.svg">
</div>

### **Corvax** — первый русскоязычный проект в мире Space Station 14.

**Wega** — первый официальный **ERP**-сервер от Corvax, где **ERP — это не обязательный элемент**, а **опциональная механика** для тех, кто хочет добавить глубины и эмоций в свою игровую сессию.  

Это репозиторий русскоязычного сервера на базе SS14. Его цель — полная локализация игры, поддержка последних изменений из основного проекта, а также развитие собственных игровых механик и контента.

---

## 🔗 Ссылки

[Наш Discord](https://discord.gg/bJrgSmKf3a) | [Наша Вики](https://station14.ru/wiki/%D0%9F%D0%BE%D1%80%D1%82%D0%B0%D0%BB:Wega) | [Steam](https://store.steampowered.com/app/1255460/Space_Station_14/) | [Клиент без Steam](https://spacestation14.io/about/nightlies/) | [Основной репозиторий](https://github.com/space-wizards/space-station-14)

---

## 📚 Документация

Вся необходимая информация о движке, контенте, разработке и игровом дизайне доступна на [официальной документации SS14](https://docs.spacestation14.io/). Там же вы найдёте полезные материалы для начинающих разработчиков и моддеров.

---

## 🤝 Контрибьют

Мы приветствуем вклад от любого желающего! Присоединяйтесь к [нашему Discord](https://discord.gg/bJrgSmKf3a), если хотите помочь.  
У нас есть [список задач](https://github.com/corvax-team/ss14-wega/issues), доступных для сообщества. Не бойтесь просить помощи — мы рады поддержать новичков.

Перед тем как отправить Pull Request, убедитесь, что он соответствует [гайду по контрибьюту](https://docs.spacestation14.com/en/general-development/codebase-info/pull-request-guidelines.html).

---

## ⚙️ Сборка

1. Склонируйте репозиторий `ss14-wega` к себе локально.
2. Запустите `RUN_THIS.py` для инициализации подмодулей и скачивания движка.
3. Соберите проект через вашу среду разработки (Visual Studio / Rider).

[Подробная инструкция по сборке проекта](https://docs.spacestation14.com/en/general-development/setup.html)

---

## 🧰 Папка `dev/` — служебные файлы форка

Личные скрипты, заметки и арт-референсы этого форка лежат в `dev/`, чтобы не засорять корень репозитория. Игровой сборке они не нужны. Все скрипты запускаются **из корня репо**:

| Что | Где | Зачем |
|---|---|---|
| `gen_adaptive_sprites.py`, `gen_rengoku_inhand.py`, `gen_urban_parallax.py` | `dev/` | Генераторы спрайтов/параллакса (`python3 dev/gen_*.py`), пишут в `Resources/Textures/` |
| `run_mapping.sh` / `run_mapping.bat`, `MAPPING_README.txt` | `dev/` | Локальный запуск для маппинга (`./dev/run_mapping.sh`), инструкция для мапперов |
| `package_windows_standalone.sh` | `dev/` | Сборка standalone-билда под Windows (`./dev/package_windows_standalone.sh`), результат в `dist/` |
| `human_dummy_reference*.png` | `dev/` | Референсы человеческой модельки (4 направления) для рисования одежды, м/ж + версии ×12 |
| `TODO.md`, `RAID_MODE.md`, `DUEL_SUPPLY_DROP.md` | `dev/` | Заметки по механикам арены (рейд-режим, аирдроп дуэлей) и список задач |
| `art_backup/`, `crate_mockups/`, `tile_previews/` | `dev/` | Бэкап старых спрайтов, мокапы ящиков, превью тайлов (`dev/tile_previews/gen_tiles.py`) |

Прайс-листы арены — `FULL_ARSENAL_PRICES.md` и `MELEE_ARSENAL_PRICES.md` — остаются в корне (см. `CLAUDE.md`).

---

## 📄 Лицензия

- Основной код Space Station 14 распространяется под лицензией **[MIT](https://github.com/corvax-team/ss14-wega/blob/master/MIT_LICENSE.TXT)**.
- Все модификации и наработки в `ss14-wega` под тегом `Corvax-Wega` и директориях `_Wega` лицензированы под **[GNU GPL v3](https://github.com/corvax-team/ss14-wega/blob/master/LICENSE.TXT)**. Это означает:
  - Производные проекты обязаны сохранять эту же лицензию.
  - Необходимо указывать оригинальных авторов и отмечать изменения.

Большинство ассетов лицензированы под [CC-BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/), если не указано иное. Метаданные с лицензией и авторством находятся в каждом `.rsi/meta.json`-файле.  
Пример: [crowbar.rsi/meta.json](https://github.com/corvax-team/ss14-wega/blob/master/Resources/Textures/Objects/Tools/crowbar.rsi/meta.json)

⚠️ Обратите внимание: часть ассетов лицензирована под [CC-BY-NC-SA 3.0](https://creativecommons.org/licenses/by-nc-sa/3.0/) или аналогичной некоммерческой лицензией. Эти ресурсы необходимо заменить или удалить для использования в коммерческих проектах.

### 🔁 Заимствованный контент из других форков

Часть контента и механик портирована из сторонних форков SS14. Код и ассеты сохраняют свои оригинальные лицензии и авторство (см. `meta.json` и заголовки файлов в соответствующих директориях):

- `_Sunrise` — контент из проекта **Sunrise**.
- `_Starlight` — контент из проекта **Starlight**.
- `_RMC14` — контент из проекта **RMC-14** (порт Colonial Marines).
- `_Lust` — контент из проекта **Lust Station**.

При переносе соблюдаются лицензии исходных проектов; изменения помечаются в коде и метаданных.

---
