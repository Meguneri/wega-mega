param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]*$')][string]$Vps,
    [Parameter(Mandatory)][string]$SshKey,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Select a new snapshot directory.' }
$key = (Resolve-Path -LiteralPath $SshKey).Path
$token = [guid]::NewGuid().ToString('N')
$remote = "/root/wega-state-$token"
$sshArgs = @('-i', $key, '-o', 'IdentitiesOnly=yes', '-o', 'BatchMode=yes')
$script = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'export-state.sh')).Replace("`r`n", "`n")
$temporary = Join-Path ([IO.Path]::GetTempPath()) "wega-state-$token.sh"
[IO.File]::WriteAllText($temporary, $script, [Text.UTF8Encoding]::new($false))
& scp @sshArgs $temporary "root@${Vps}:$remote.sh"
if ($LASTEXITCODE -ne 0) { throw 'Snapshot exporter upload failed.' }
& ssh @sshArgs "root@$Vps" "bash $remote.sh $remote"
if ($LASTEXITCODE -ne 0) { throw 'Snapshot failed; no deployment settings replaced.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
foreach ($file in @('preferences.live.db', 'server_config.toml', 'deploy.env', 'userdata.tar.gz', 'manifest.json')) {
    & scp @sshArgs "root@${Vps}:$remote/$file" $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw "Snapshot download failed: $file" }
}
$manifest = Get-Content (Join-Path $OutputDirectory 'manifest.json') -Raw | ConvertFrom-Json
foreach ($file in $manifest.files.PSObject.Properties) {
    $actual = (Get-FileHash (Join-Path $OutputDirectory $file.Name) -Algorithm SHA256).Hash
    if ($actual -ine $file.Value) { throw "Snapshot checksum mismatch: $($file.Name)" }
}
Write-Host "Verified snapshot: $OutputDirectory"
Write-Host "Remote snapshot retained: $remote"
