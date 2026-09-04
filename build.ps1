param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$SkipNative
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$toolRoot = Join-Path $repoRoot '.tools\net48-reference'
$referenceAssembly = Join-Path $toolRoot 'build\.NETFramework\v4.8\mscorlib.dll'
$audioCodecRoot = Join-Path $repoRoot '.tools\audio-codecs'
$audioCodecAssembly = Join-Path $audioCodecRoot 'Fmod5Sharp\lib\netstandard2.0\Fmod5Sharp.dll'
$vorbisDecoderAssembly = Join-Path $audioCodecRoot 'legacy\NVorbis\lib\net35\NVorbis.dll'
$fmod5SharpLicense = Join-Path $repoRoot 'licenses\Fmod5Sharp-LICENSE.txt'
$msbuild = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'

if (-not (Test-Path -LiteralPath $msbuild)) {
    throw '.NET Framework MSBuild was not found.'
}
if (-not $SkipNative -and -not (Test-Path -LiteralPath $vswhere)) {
    throw 'Visual Studio C++ build tools were not found.'
}
if (-not (Test-Path -LiteralPath $fmod5SharpLicense -PathType Leaf)) {
    throw "Fmod5Sharp license file was not found: $fmod5SharpLicense"
}

if (-not (Test-Path -LiteralPath $referenceAssembly)) {
    $downloadRoot = Join-Path $repoRoot '.tools'
    $packagePath = Join-Path $downloadRoot 'net48-reference.zip'
    New-Item -ItemType Directory -Force -Path $downloadRoot | Out-Null
    Write-Host 'Downloading Microsoft .NET Framework 4.8 reference assemblies (build-only)...'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://www.nuget.org/api/v2/package/Microsoft.NETFramework.ReferenceAssemblies.net48/1.0.3' -OutFile $packagePath
    if (Test-Path -LiteralPath $toolRoot) {
        Remove-Item -LiteralPath $toolRoot -Recurse -Force
    }
    Expand-Archive -LiteralPath $packagePath -DestinationPath $toolRoot
    Remove-Item -LiteralPath $packagePath -Force
}

if (-not (Test-Path -LiteralPath $audioCodecAssembly) -or
    -not (Test-Path -LiteralPath $vorbisDecoderAssembly)) {
    $downloadRoot = Join-Path $repoRoot '.tools'
    $nuget = Join-Path $downloadRoot 'nuget.exe'
    New-Item -ItemType Directory -Force -Path $downloadRoot | Out-Null
    if (-not (Test-Path -LiteralPath $nuget)) {
        Write-Host 'Downloading NuGet for managed FSB5 Vorbis support (build-only)...'
        Invoke-WebRequest -UseBasicParsing `
            -Uri 'https://dist.nuget.org/win-x86-commandline/v6.11.1/nuget.exe' `
            -OutFile $nuget
    }
    Write-Host 'Restoring managed audio codec dependencies...'
    if (-not (Test-Path -LiteralPath $audioCodecAssembly)) {
        & $nuget install Fmod5Sharp -Version 2.0.2 `
            -OutputDirectory $audioCodecRoot -ExcludeVersion `
            -DependencyVersion Lowest -Framework net48 -NonInteractive
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    if (-not (Test-Path -LiteralPath $vorbisDecoderAssembly)) {
        & $nuget install NVorbis -Version 0.8.6 `
            -OutputDirectory (Join-Path $audioCodecRoot 'legacy') `
            -ExcludeVersion -DependencyVersion Lowest `
            -Framework net48 -NonInteractive
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}

if (-not $SkipNative) {
    $nativeMsbuild = & $vswhere -latest -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $nativeMsbuild) { throw 'Visual Studio C++ MSBuild was not found.' }
    $nativeProject = Join-Path $repoRoot 'native\DirectCompositionRenderer\DirectCompositionRenderer.vcxproj'
    & $nativeMsbuild $nativeProject /t:Build /p:Configuration=$Configuration `
        /p:Platform=x64 /m:1 /nodeReuse:false /nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# The app and test projects share the same output directory. Legacy MSBuild can
# report a false parallel-build failure while both projects copy their outputs.
& $msbuild (Join-Path $repoRoot 'RainWorldDesktopPet.sln') /t:Build /p:Configuration=$Configuration /m:1 /nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$licenseOutputDirectory = Join-Path $repoRoot "artifacts\$Configuration\licenses"
New-Item -ItemType Directory -Force -Path $licenseOutputDirectory | Out-Null
Copy-Item -LiteralPath $fmod5SharpLicense -Destination (Join-Path $licenseOutputDirectory 'Fmod5Sharp-LICENSE.txt') -Force

if (-not $SkipTests) {
    & (Join-Path $repoRoot "artifacts\$Configuration\RainWorldDesktopPet.Tests.exe")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
