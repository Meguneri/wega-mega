param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]*$')][string]$Vps,
    [Parameter(Mandatory)][string]$SshKey,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._/-]*$')][string]$Branch = 'codex/upstream-integration',
    [string]$SettingsDirectory = (Join-Path $PSScriptRoot '.cache/deployment')
)
$ErrorActionPreference = 'Stop'
$database = Join-Path $SettingsDirectory 'preferences.live.db'
$config = Join-Path $SettingsDirectory 'server_config.toml'
foreach ($file in @($database, $config)) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Saved deployment file missing: $file"
    }
}
# Готовая сборка не передаётся: новый VPS получает исходники выбранной ветки Git.
& (Join-Path $PSScriptRoot 'deploy.ps1') -Vps $Vps -SshKey $SshKey -Branch $Branch `
    -Database $database -ServerConfig $config
