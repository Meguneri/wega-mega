# Wega SS14 — Развёртывание и обслуживание приватного сервера

> Runbook по поднятию и администрированию приватного сервера этого форка (ss14-wega).
> Составлен по итогам сессии деплоя **14.07.2026**. Сервер: HOSTKEY VPS, Ubuntu 22.04, Финляндия.
>
> ⚠️ **Файл содержит инфраструктурные детали (IP, пути).** Если репозиторий публичный —
> не коммить его в паблик: добавь в `.gitignore` или держи локально/в приватном месте.
> **Паролей здесь нет и быть не должно.**

---

## 0. Что за сервер

| Параметр | Значение |
|---|---|
| Провайдер | HOSTKEY, тариф **v2-nano** (2 vCPU / 4 GB RAM / 60 GB NVMe) |
| ОС | Ubuntu 22.04 |
| Регион | FI (Финляндия) |
| IP | `82.26.171.55` |
| Игровой порт | `1212` (TCP + UDP) |
| Каталоги | код: `/opt/wega/server`, данные: `/opt/wega/data` |
| Запуск | systemd-юнит `wega.service` |

Заметки по железу: для **двоих** nano хватает. На тяжёлых моментах (генерация подземелий, старт
раунда) на 2 ядрах бывают всплески `MainLoop: Cannot keep up!` — это норма. Если упрётесь по
онлайну/лагам — апгрейд до **v2-mini** (4 vCPU / 8 GB) прямо в панели HOSTKEY без переустановки.

SS14 упирается в **однопоточную** производительность (главный игровой цикл), поэтому важна частота
ядра, а не их число. Форк тяжёлый — 4 ГБ это реальный минимум; на nano обязателен swap (см. ниже).

---

## 1. Доступ по SSH (ключ + смена пароля)

Вход по ключу через **Secretive** (ключ в Secure Enclave, подтверждение Touch ID).

На Mac (`~/.ssh/config`):
```
Host 82.26.171.55
    IdentityAgent ~/Library/Containers/com.maxgoedjen.Secretive.SecretAgent/Data/socket.ssh
```
Публичный ключ (из Secretive «Copy Public Key») — на сервере в `~/.ssh/authorized_keys`, одной строкой,
`chmod 600`. Проверка: `ssh root@82.26.171.55` → должен всплыть Touch ID, без пароля.

Смена засвеченного пароля и (по желанию) отключение парольного входа:
```bash
passwd
# только ПОСЛЕ проверки входа по ключу:
sed -i 's/^#\?PasswordAuthentication.*/PasswordAuthentication no/' /etc/ssh/sshd_config
sed -i 's/^#\?PermitRootLogin.*/PermitRootLogin prohibit-password/' /etc/ssh/sshd_config
systemctl restart ssh
```

---

## 2. Первичная настройка VPS

```bash
# swap 2 ГБ — страховка от OOM на 4 ГБ
fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab

# не спрашивать про рестарт сервисов при apt
echo '$nrconf{restart} = "a";' > /etc/needrestart/conf.d/no-prompt.conf

# зависимости
apt update && apt install -y ufw git python3 ffmpeg curl tmux sqlite3

# фаервол: SSH + игровой порт
ufw allow 22/tcp && ufw allow 1212 && ufw --force enable

# .NET 10 SDK (в apt для 22.04 его нет)
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet.sh
bash /tmp/dotnet.sh --channel 10.0 --install-dir /opt/dotnet
ln -sf /opt/dotnet/dotnet /usr/local/bin/dotnet
dotnet --version   # 10.x
```
> Нужен именно **.NET 10 SDK** (`global.json` → 10.0.100). `ffmpeg` — из apt; `yt-dlp` медиаплеер
> скачает сам при первом поиске.

---

## 3. Клон и сборка

RobustToolbox — git-сабмодуль, поэтому `--recurse-submodules`. Полная `.git` ~4 ГБ → shallow-клон.
```bash
mkdir -p /opt/wega && cd /opt/wega
git clone --depth 1 --branch arena-mode-develop --recurse-submodules --shallow-submodules \
  https://github.com/Meguneri/wega-mega.git server
cd server
dotnet build -c Release          # первая сборка несколько минут
mkdir -p /opt/wega/data
```

---

## 4. Конфиг сервера — `/opt/wega/server/server_config.toml`

```toml
[net]
port = 1212

[game]
hostname = "Wega - privatka"
maxplayers = 8
lobbyenabled = true

[auth]
mode = 1          # 1 = Optional: пускает и с аккаунтом, и без

[status]
enabled = true

[whitelist]
enabled = true    # приватка: пускает только вайтлист + админов
```
Записывать в файл через heredoc (`cat > ... <<'EOF' ... EOF`), **а не построчно в шелл**.

---

## 5. Запуск (systemd)

`/etc/systemd/system/wega.service`:
```ini
[Unit]
Description=Wega SS14 server
After=network.target

[Service]
WorkingDirectory=/opt/wega/server
Environment=HOME=/root
Environment=DOTNET_gcServer=1
ExecStart=/usr/local/bin/dotnet run --project Content.Server -c Release --no-build -- --config-file server_config.toml --data-dir /opt/wega/data
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```
```bash
systemctl daemon-reload && systemctl enable --now wega
```
Управление:
```bash
journalctl -u wega -f     # логи (Ctrl+C — выход из просмотра, сервис живёт)
systemctl restart wega    # перезапуск
systemctl stop wega       # остановить
systemctl status wega     # статус
```
Готовность в логах: `Server Version ... -> Ready`. `shutdown` из игры → systemd поднимет сам.

### tmux — когда нужна интерактивная серверная консоль
Некоторые команды **только серверные** (`promotehost`, SERVERONLY-cvar'ы вроде `whitelist.enabled`,
надёжный `kicknonwhitelisted`). Под systemd консоли нет. Чтобы получить её временно:
```bash
systemctl stop wega
cd /opt/wega/server && tmux new -s wega
dotnet run --project Content.Server -c Release --no-build -- --config-file server_config.toml --data-dir /opt/wega/data
# в консоли '>' вводишь server-only команды
# отцепиться: Ctrl+b, затем d ; вернуться: tmux attach -t wega
```
Потом обратно на systemd: `Ctrl+C` в tmux → `systemctl start wega`.

---

## 6. Подключение клиентом

Лаунчер SS14 → **Direct Connect** → `82.26.171.55` (`ss14://82.26.171.55`). Лаунчер сам скачает
клиент с сервера (ACZ). **Клиент должен быть того же коммита, что и сервер** — иначе отказ по хэшу
контента.

---

## 7. Вайтлист (приватка)

`whitelist.enabled` — **SERVERONLY** cvar: из игровой консоли не ставится («не зарегистрирован»).
Включать в `server_config.toml` (см. §4) или в серверной консоли `cvar whitelist.enabled true`.

Управление игроками (ник — точный SS14-аккаунт; работает из **игровой** консоли под админом):
```
whitelistadd <ник>        # добавить
whitelistremove <ник>     # убрать
kicknonwhitelisted        # выгнать всех не из списка (серверная консоль надёжнее)
```
- **Админы игнорируют вайтлист** автоматически — в список их вносить не нужно.
- Добавление в вайтлист **никого не кикает**; уже сидящих трогает только `kicknonwhitelisted`.
- Проверить, кто в списке: команды «показать список» нет; `whitelistadd <ник>` ответит «уже в
  вайтлисте», если он там. Полный список — только запросом к БД.
- Пароля на вход в SS14 нет — вайтлист его заменяет.
- Убрать «чужих» проще всего: `kick <ник>` (точечно) или `systemctl restart wega` (все переподключатся,
  зайдут только вайтлист+админы).

---

## 8. Админы

### Выдать себе постоянного полного хоста (через БД) — надёжнее панели
Флаги в БД — построчно (`admin_flag`), имена — enum'ы В ВЕРХНЕМ регистре. Полный набор = 24 флага.
```bash
systemctl stop wega
DB=$(ls /opt/wega/data/*.db /opt/wega/data/*.sqlite 2>/dev/null | head -1)
sqlite3 "$DB" "
UPDATE admin SET suspended = 0, deadminned = 0;
DELETE FROM admin_flag;
INSERT INTO admin_flag (admin_id, flag, negative)
SELECT user_id, f.flag, 0 FROM admin CROSS JOIN (
 SELECT 'ADMIN' flag UNION ALL SELECT 'BAN' UNION ALL SELECT 'DEBUG' UNION ALL SELECT 'FUN'
 UNION ALL SELECT 'PERMISSIONS' UNION ALL SELECT 'SERVER' UNION ALL SELECT 'SPAWN' UNION ALL SELECT 'VAREDIT'
 UNION ALL SELECT 'MAPPING' UNION ALL SELECT 'LOGS' UNION ALL SELECT 'ROUND' UNION ALL SELECT 'QUERY'
 UNION ALL SELECT 'ADMINHELP' UNION ALL SELECT 'VIEWNOTES' UNION ALL SELECT 'EDITNOTES' UNION ALL SELECT 'MASSBAN'
 UNION ALL SELECT 'STEALTH' UNION ALL SELECT 'ADMINCHAT' UNION ALL SELECT 'PII' UNION ALL SELECT 'MODERATOR'
 UNION ALL SELECT 'ADMINWHO' UNION ALL SELECT 'NAMECOLOR' UNION ALL SELECT 'PLAYTIME' UNION ALL SELECT 'HOST'
) f;"
systemctl start wega
```
(Выдаёт полный хост ВСЕМ записям в `admin` — на приватке это только ты.) После — зайти, `readmin`.

### ⚠️ Главный подвох: флаг `HOST` ≠ «все права»
`HasFlag` — строгая побитовая проверка, без спец-обработки Host. Каждой команде нужен **свой** флаг:
- **F7 (админ-меню)** → флаг `ADMIN`
- **F5 / F6 / F8 (спавн сущностей / тайлов / декалей)** → флаг `SPAWN`
- **`forcemap`, `loadgamemap`** → флаг `ROUND`
- `scsi`, `promotehost` → флаг `HOST`

Если выдать только `HOST` — почти ничего не работает (`readmin` покажет ~43 команды = только
публичные). **Нужно выдать ВСЕ нужные флаги.**

### Через панель (альтернатива БД)
Панель разрешений → Админы → запись игрока → у каждого флага три состояния **`I` / `-` / `+`**.
Ставь **`+` (разрешить)** на нужные флаги, **не `I`** (наследовать — без ранга наследовать не от чего).
→ Сохранить → **переподключиться**.

### deadmin / readmin
Админ, заходя играть персонажем, обычно **задеадминен** — тулзы «спят». Команда `readmin` в консоли
включает админ-режим (`Updated admin status: True//HOST`). Делать после каждого захода персонажем.

### promotehost
`promotehost <ник>` из **серверной** консоли — мгновенно полный хост (`Everything`), но **на сессию**
(после рестарта слетает). Для постоянного — БД/панель выше.

### Новые админы — набор флагов под роль
- **Со-хост (полный):** все 24 флага.
- **Модератор:** `ADMIN`, `BAN`, `ADMINHELP`, `MODERATOR`, `ADMINWHO`, `VIEWNOTES`, при желании `SPAWN`.
- **Опасные флаги — только полностью доверенным:**
  - `PERMISSIONS` — редактирование других админов (может снять права тебе);
  - `HOST` — `scsi` (**shell-команды на самом сервере!**), `promotehost` — уровень владельца.

---

## 9. Обновление сервера (залить новый код)

```bash
cd /opt/wega/server
git pull
dotnet build -c Release
systemctl restart wega
```
⚠️ Клиенты должны обновиться до того же коммита (ACZ подтянет автоматически при заходе).
**Важно:** локальные незакоммиченные правки на Mac (арена/медиаплеер/стены) на сервер НЕ приедут,
пока не сделаешь `git commit` + `git push` в ветку `arena-mode-develop`.

---

## 10. Частые «пугалки» в логах (безобидное)

- `[WARN] net.ent: Got late MsgEntity!` — сетевой лаг/пинг (сервер в FI). Косметика.
- `[WARN] net: Received unhandled library message Acknowledge/Ping/Pong from <ip>` — чужой хост стучит
  в открытый порт (сканер/бот). Не атака. Задолбает — `ufw deny from <ip>`.
- `[WARN] eng: MainLoop: Cannot keep up!` — всплеск нагрузки (старт раунда/генерация). Норма на 2 ядрах.
- `[ERRO]` при `shutdown` (`network_configurator`, `Failed to attach entity`, follower в null-space) —
  безобидная уборка сущностей при остановке.

## 11. Известные реальные баги (TODO)

- **`raid_stash` / `IThresholdTrigger`** — при загрузке личного схрона рейдера из снапшота падает
  десериализация: `SerializeBoxContents` сохраняет весь ящик вместе с `Destructible`, а полиморфный
  триггер порога при round-trip теряет `!type:`-тег → схрон не восстанавливается. Файл:
  `Content.Server/_Wega/Raid/Systems/RaidStashSystem.cs`. Фикс: сохранять только СОДЕРЖИМОЕ ящика
  (спавнить `RaidStashBox` заново и вкладывать предметы), либо убрать `Destructible` с ящика.

---

## 12. Безопасность / напоминания

- Root-пароль из чата считать скомпрометированным → сменить (`passwd`), лучше жить по SSH-ключу и
  выключить парольный вход.
- Этот файл не коммитить в публичный репо (IP/инфраструктура).
- `PERMISSIONS` и `HOST` — только полностью доверенным людям.
- Резервная копия важного — файл БД `/opt/wega/data/*.db` (баны, вайтлист, админы, схроны).

---

_Составлено 14.07.2026 в ходе сессии развёртывания приватного сервера Wega SS14._
