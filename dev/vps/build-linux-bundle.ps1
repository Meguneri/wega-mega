param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Select a new output directory.' }
$revision = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $revision -notmatch '^[0-9a-f]{40}$') { throw 'Cannot resolve source commit.' }
& git -C $root diff --quiet HEAD --
if ($LASTEXITCODE -ne 0) { throw 'Commit tracked changes before packaging.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
Push-Location $root
try {
    foreach ($project in @('Content.Server', 'Content.Client')) {
        Write-Host "Publishing Linux Release: $project"
        $destination = Join-Path $output "stage/bin/$project"
        & dotnet publish "$project/$project.csproj" -c Release -r linux-x64 --self-contained false `
            -p:TargetOS=Linux -p:FullRelease=false -p:PublishSingleFile=false -p:PublishTrimmed=false `
            -p:ErrorOnDuplicatePublishOutputFiles=false -m:1 -p:UseSharedCompilation=false `
            --disable-build-servers -o $destination *> (Join-Path $output "$project.log")
        if ($LASTEXITCODE -ne 0) { throw "Publishing failed; see $output/$project.log" }
    }
    & git diff --quiet HEAD --
    if ($LASTEXITCODE -ne 0) { throw 'Source changed while publishing; refusing to label the bundle.' }
    @'
import hashlib
from pathlib import Path
import sys
import tarfile

root, output, revision = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
def include(info):
    parts = Path(info.name).parts
    if 'data' in parts or 'logs' in parts or info.name.endswith(('.db', '.db-wal', '.db-shm')):
        return None
    if info.name.startswith('bin/') and info.name.endswith('.toml'):
        return None
    info.uid = info.gid = 0
    info.uname = info.gname = ''
    info.mode = 0o755 if info.isdir() or Path(info.name).name in ('Content.Server', 'Content.Client') else 0o644
    return info
bundle = output / 'wega-release.tar.gz'
with tarfile.open(bundle, 'w:gz', compresslevel=3) as archive:
    for directory in ['bin/Content.Server', 'bin/Content.Client']:
        archive.add(output / 'stage' / directory, arcname=directory, filter=include)
    for directory in ['Resources', 'RobustToolbox/Resources']:
        archive.add(root / directory, arcname=directory, filter=include)
(output / 'wega-release.commit').write_text(revision + '\n', encoding='utf-8', newline='\n')
with bundle.open('rb') as stream:
    digest = hashlib.file_digest(stream, 'sha256').hexdigest()
(output / 'wega-release.sha256').write_text(digest + '  wega-release.tar.gz\n', encoding='utf-8', newline='\n')
print('Linux bundle verified:', digest)
'@ | & python - $root $output $revision
    if ($LASTEXITCODE -ne 0) { throw 'Bundle creation failed.' }
    Write-Host "Ready Linux bundle: $output"
} finally {
    Pop-Location
}
