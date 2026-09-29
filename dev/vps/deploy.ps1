param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]*$')][string]$Vps,
    [Parameter(Mandatory)][string]$SshKey,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._/-]*$')][string]$Branch = 'codex/upstream-integration',
    [string]$Database,
    [string[]]$Admins = @('Meguneri', 'arhont1234'),
    [string]$BundleDirectory,
    [string]$ServerConfig
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$Database) { $Database = Join-Path $root 'bin/Content.Server/data/preferences.db' }
if (!$ServerConfig) { $ServerConfig = Join-Path $PSScriptRoot 'server_config.toml' }
$key = (Resolve-Path -LiteralPath $SshKey).Path
$stage = Join-Path ([IO.Path]::GetTempPath()) ('wega-deploy-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$snapshot = Join-Path $stage 'preferences.snapshot.db'
& python (Join-Path $PSScriptRoot 'prepare_database.py') --database $Database --output $snapshot --admins @Admins
if ($LASTEXITCODE -ne 0) { throw 'Database preparation failed; nothing sent to VPS.' }
$sshArgs = @('-i', $key, '-o', 'IdentitiesOnly=yes', '-o', 'BatchMode=yes')
& ssh @sshArgs "root@$Vps" 'test ! -e /var/lib/wega/data/preferences.db && test ! -e /etc/wega/deploy.env && install -d -m 700 /root/wega-deploy'
if ($LASTEXITCODE -ne 0) { throw 'VPS is inaccessible or already deployed; refusing to overwrite it.' }
$files = @('bootstrap.sh','update.sh','wega.service','wega-update.service','wega-update.timer') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$files += $snapshot
& scp @sshArgs @files "root@${Vps}:/root/wega-deploy/"
if ($LASTEXITCODE -ne 0) { throw 'Script/database upload failed.' }
& scp @sshArgs $ServerConfig "root@${Vps}:/root/wega-deploy/server_config.toml"
if ($LASTEXITCODE -ne 0) { throw 'Config upload failed.' }
if ($BundleDirectory) {
    $bundleFiles = @('wega-release.tar.gz','wega-release.commit','wega-release.sha256') | ForEach-Object { (Resolve-Path -LiteralPath (Join-Path $BundleDirectory $_)).Path }
    & scp @sshArgs @bundleFiles "root@${Vps}:/root/wega-deploy/"
    if ($LASTEXITCODE -ne 0) { throw 'Bundle upload failed.' }
}
# Временная служба продолжает установку после закрытия SSH.
& ssh @sshArgs "root@$Vps" "bash -n /root/wega-deploy/bootstrap.sh && bash -n /root/wega-deploy/update.sh && systemd-run --unit=wega-initial-deploy --property=TimeoutStartSec=7200 /bin/bash /root/wega-deploy/bootstrap.sh $Branch"
if ($LASTEXITCODE -ne 0) { throw 'Deployment could not be started.' }
Write-Host "Deployment started. Status: ssh -i `"$key`" root@$Vps 'journalctl -u wega-initial-deploy -n 30 --no-pager'"
Write-Host "The server is NOT confirmed ready yet. Verify Ready, /info, and /manifest.txt before connecting."
Write-Host "Local database snapshot retained: $snapshot"
