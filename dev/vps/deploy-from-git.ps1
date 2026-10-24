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
$manifestPath = Join-Path $SettingsDirectory 'manifest.json'
if (Test-Path -LiteralPath $manifestPath) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    foreach ($entry in $manifest.files.PSObject.Properties) {
        $actual = (Get-FileHash (Join-Path $SettingsDirectory $entry.Name) -Algorithm SHA256).Hash
        if ($actual -ine $entry.Value) { throw "Saved deployment checksum mismatch: $($entry.Name)" }
    }
}
$extra = @{}
$userData = Join-Path $SettingsDirectory 'userdata.tar.gz'
if (Test-Path -LiteralPath $userData) { $extra.UserDataArchive = $userData }
& (Join-Path $PSScriptRoot 'deploy.ps1') -Vps $Vps -SshKey $SshKey -Branch $Branch `
    -Database $database -ServerConfig $config @extra
