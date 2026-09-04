param(
    [string]$Version = 'v0.1.0',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

if ($Version -ne 'continuous' -and
    $Version -notmatch '^v?\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw "Version must be continuous or look like v0.1.0: $Version"
}
if ($Version -ne 'continuous' -and -not $Version.StartsWith('v')) {
    $Version = 'v' + $Version
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$packageRoot = Join-Path $repoRoot 'artifacts\package'
$packageName = "SlugcatInMyMonitor-$Version-win-x64"
$stagingRoot = Join-Path $packageRoot $packageName
$archivePath = Join-Path $packageRoot ($packageName + '.zip')
$checksumPath = $archivePath + '.sha256'

if (-not $SkipBuild) {
    & (Join-Path $repoRoot 'build.ps1') -Configuration Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$releaseRoot = Join-Path $repoRoot 'artifacts\Release'
$releaseFiles = @(
    'SlugcatInMyMonitor.exe',
    'SlugcatInMyMonitor.exe.config',
    'SlugcatInMyMonitor.DirectComposition.dll',
    'Fmod5Sharp.dll',
    'NVorbis.dll',
    'NAudio.Core.dll',
    'OggVorbisEncoder.dll',
    'Microsoft.Bcl.AsyncInterfaces.dll',
    'System.Buffers.dll',
    'System.Memory.dll',
    'System.Numerics.Vectors.dll',
    'System.Runtime.CompilerServices.Unsafe.dll',
    'System.Text.Encodings.Web.dll',
    'System.Text.Json.dll',
    'System.Threading.Tasks.Extensions.dll',
    'System.ValueTuple.dll',
    'THIRD-PARTY-NOTICES.md',
    'licenses\Fmod5Sharp-LICENSE.txt'
)
$repositoryFiles = @(
    'README.md',
    'README.ko.md',
    'LICENSE'
)

$packageFiles = @(
    foreach ($name in $releaseFiles) {
        [PSCustomObject]@{ Source = Join-Path $releaseRoot $name; Name = $name }
    }
    foreach ($name in $repositoryFiles) {
        [PSCustomObject]@{ Source = Join-Path $repoRoot $name; Name = $name }
    }
)
foreach ($file in $packageFiles) {
    if (-not (Test-Path -LiteralPath $file.Source -PathType Leaf)) {
        throw "Required package file is missing: $($file.Source)"
    }
}

New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null

foreach ($file in $packageFiles) {
    $destinationPath = Join-Path $stagingRoot $file.Name
    $destinationDirectory = Split-Path -Parent $destinationPath
    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
    Copy-Item -LiteralPath $file.Source -Destination $destinationPath
}

Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $archivePath -Force

# Re-open the final archive so a successful copy cannot hide an incomplete ZIP.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $archiveFiles = @(
        $archive.Entries |
            Where-Object { -not [string]::IsNullOrEmpty($_.Name) } |
            ForEach-Object { $_.FullName.Replace('\', '/') }
    )
}
finally {
    $archive.Dispose()
}

$expectedArchiveFiles = @(
    $packageFiles | ForEach-Object { $_.Name.Replace('\', '/') }
)
$missingArchiveFiles = @($expectedArchiveFiles | Where-Object { $archiveFiles -notcontains $_ })
$unexpectedArchiveFiles = @($archiveFiles | Where-Object { $expectedArchiveFiles -notcontains $_ })
if ($missingArchiveFiles.Count -gt 0 -or $unexpectedArchiveFiles.Count -gt 0) {
    throw "Package verification failed. Missing: $($missingArchiveFiles -join ', '); Unexpected: $($unexpectedArchiveFiles -join ', ')"
}

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Encoding ascii -NoNewline `
    -Value "$hash  $([IO.Path]::GetFileName($archivePath))"

Write-Host "Created $archivePath"
Write-Host "Verified $($archiveFiles.Count) packaged files"
Write-Host "SHA-256 $hash"
