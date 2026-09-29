param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]*$')][string]$Vps,
    [Parameter(Mandatory)][string]$SshKey,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot '.cache' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Output directory already exists. Select a new directory.' }
$key = (Resolve-Path -LiteralPath $SshKey).Path
$token = [guid]::NewGuid().ToString('N')
$remoteScript = "/root/wega-export-$token.sh"
$remoteBundle = "/root/wega-bundle-$token"
$sshArgs = @('-i', $key, '-o', 'IdentitiesOnly=yes', '-o', 'BatchMode=yes')
& scp @sshArgs (Join-Path $PSScriptRoot 'export-bundle.sh') "root@${Vps}:$remoteScript"
if ($LASTEXITCODE -ne 0) { throw 'Exporter upload failed.' }
& ssh @sshArgs "root@$Vps" "bash $remoteScript $remoteBundle"
if ($LASTEXITCODE -ne 0) { throw 'Bundle export failed.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
foreach ($file in @('wega-release.tar.gz','wega-release.commit','wega-release.sha256')) {
    & scp @sshArgs "root@${Vps}:$remoteBundle/$file" $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Bundle download failed. Partial files retained for inspection.' }
}
$expected = ((Get-Content (Join-Path $OutputDirectory 'wega-release.sha256') -Raw) -split '\s+')[0]
$actual = (Get-FileHash (Join-Path $OutputDirectory 'wega-release.tar.gz') -Algorithm SHA256).Hash
if ($actual -ine $expected) { throw 'Bundle checksum mismatch.' }
Write-Host "Verified bundle: $OutputDirectory"
Write-Host "Remote export retained: $remoteBundle"
