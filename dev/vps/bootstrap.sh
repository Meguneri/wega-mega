#!/bin/bash
# Первый запуск на чистом Ubuntu 24.04; существующие данные не перезаписываются.
set -euo pipefail
umask 077
stage=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
branch=${1:-codex/upstream-integration}
[[ "$branch" =~ ^[A-Za-z0-9][A-Za-z0-9._/-]*$ ]] || exit 1
if [ "$(id -u)" != 0 ]; then echo 'Run as root.' >&2; exit 1; fi
if [ -e /var/lib/wega/data/preferences.db ] || [ -e /etc/wega/deploy.env ]; then
  echo 'Existing deployment detected. Refusing to overwrite it; use wega-update.' >&2
  exit 1
fi
source /etc/os-release
if [ "$ID" != ubuntu ] || [ "$VERSION_ID" != 24.04 ]; then
  echo 'This bootstrap supports Ubuntu 24.04 only.' >&2; exit 1
fi
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y git curl ca-certificates python3 sqlite3 libicu74 libssl3t64 libzstd1 libatomic1 unzip
git check-ref-format --branch "$branch" >/dev/null
python3 -c 'import tomllib,sys; c=tomllib.load(open(sys.argv[1],"rb")); assert c["auth"]["mode"]==1 and c["auth"]["allowlocal"] is False, "Verified authentication required"; assert c["net"]["port"]==1212, "This kit uses port 1212"' "$stage/server_config.toml"
id wega >/dev/null 2>&1 || useradd --system --create-home --home-dir /var/lib/wega --shell /usr/sbin/nologin wega
install -d -m 755 /opt/dotnet /opt/wega /opt/wega/releases /etc/wega
install -d -m 700 -o wega -g wega /var/lib/wega/data /var/lib/wega/backups
if ! swapon --show --noheadings | grep -q .; then
  if [ ! -e /var/lib/wega-build.swap ]; then
    fallocate -l 4G /var/lib/wega-build.swap
    chmod 600 /var/lib/wega-build.swap
    mkswap /var/lib/wega-build.swap
  fi
  swapon /var/lib/wega-build.swap
  grep -qF '/var/lib/wega-build.swap none swap sw 0 0' /etc/fstab || echo '/var/lib/wega-build.swap none swap sw 0 0' >> /etc/fstab
fi
if [ ! -x /opt/dotnet/dotnet ]; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /opt/wega/dotnet-install.sh
  (umask 022; bash /opt/wega/dotnet-install.sh --channel 10.0 --install-dir /opt/dotnet)
fi
chmod -R a+rX /opt/dotnet
git clone --depth 1 --filter=blob:none --no-checkout --single-branch --branch "$branch" https://github.com/Meguneri/wega-mega.git /opt/wega/repository
target=$(git -C /opt/wega/repository rev-parse HEAD)
if [ -f "$stage/wega-release.tar.gz" ]; then
  (cd "$stage" && sha256sum -c wega-release.sha256)
  bundle_commit=$(tr -d '\r\n' < "$stage/wega-release.commit")
  if [ "$bundle_commit" = "$target" ]; then
    release=/opt/wega/releases/$target
    install -d -m 755 "$release"
    python3 -c 'import tarfile,sys; t=tarfile.open(sys.argv[1]); t.extractall(sys.argv[2],filter="data")' "$stage/wega-release.tar.gz" "$release"
    test -f "$release/bin/Content.Server/Content.Server.dll"
    test -f "$release/bin/Content.Client/Content.Client.dll"
    test -d "$release/Resources"
    touch "$release/.build-complete"
  else
    echo 'Cached bundle is for another commit; compiling the requested branch instead.'
  fi
fi
chown -R wega:wega /opt/wega/repository /opt/wega/releases /var/lib/wega
install -m 750 "$stage/update.sh" /usr/local/sbin/wega-update
install -m 750 "$stage/install-release.sh" /usr/local/sbin/wega-install-release
install -m 640 -o root -g wega "$stage/server_config.toml" /etc/wega/server_config.toml
printf 'WEGA_BRANCH=%q\n' "$branch" > /etc/wega/deploy.env
chmod 600 /etc/wega/deploy.env
python3 -c 'import sqlite3,sys; c=sqlite3.connect("file:"+sys.argv[1]+"?mode=ro",uri=True); assert c.execute("pragma integrity_check").fetchone()[0]=="ok"; assert not c.execute("pragma foreign_key_check").fetchall()' "$stage/preferences.snapshot.db"
install -m 600 -o wega -g wega "$stage/preferences.snapshot.db" /var/lib/wega/data/preferences.db
install -m 600 "$stage/preferences.snapshot.db" /var/lib/wega/backups/preferences.imported.db
for unit in wega.service wega-update.service wega-update.timer; do
  install -m 644 "$stage/$unit" "/etc/systemd/system/$unit"
done
systemctl daemon-reload
systemctl enable wega.service
/usr/local/sbin/wega-install-release
systemctl enable --now wega-update.timer
