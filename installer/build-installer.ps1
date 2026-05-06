[CmdletBinding()]
param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',

    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [switch]$SelfContained,

    [string]$Version
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir '..')
$projectPath = Join-Path $repoRoot 'DesktopCapsules.csproj'
$publishMode = if ($SelfContained) { 'self-contained' } else { 'framework-dependent' }
$publishDir = Join-Path $repoRoot "publish\$Runtime-$publishMode"
$distDir = Join-Path $repoRoot 'dist'
$issPath = Join-Path $scriptDir 'DesktopCapsules.iss'

if (-not $Version) {
    [xml]$projectXml = Get-Content $projectPath
    $Version = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = '0.1.0'
    }
}

$publishArgs = @(
    'publish',
    $projectPath,
    '-c', $Configuration,
    '-r', $Runtime,
    '-o', $publishDir
)

if ($SelfContained) {
    $publishArgs += @(
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true'
    )
}
else {
    $publishArgs += @('--self-contained', 'false')
}

Write-Host "Publishing Desktop Capsules ($Configuration, $Runtime, $publishMode)..."
& dotnet @publishArgs

$iscc = (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )

    $iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if (-not $iscc) {
    throw @"
Inno Setup 6 is required to build the installer.

Install it with:
  winget install JRSoftware.InnoSetup

Then rerun:
  .\installer\build-installer.ps1

For a larger installer that does not require users to install .NET separately:
  .\installer\build-installer.ps1 -SelfContained
"@
}

New-Item -ItemType Directory -Path $distDir -Force | Out-Null

$requiresRuntime = if ($SelfContained) { 'false' } else { 'true' }
$innoArgs = @(
    "/DAppVersion=$Version",
    "/DPublishDir=$publishDir",
    "/DOutputDir=$distDir",
    "/DRuntimeMode=$publishMode",
    "/DRequiresRuntime=$requiresRuntime",
    $issPath
)

Write-Host "Building installer with Inno Setup..."
& $iscc @innoArgs

Write-Host ""
Write-Host "Installer output:"
Get-ChildItem -Path $distDir -Filter "DesktopCapsules-$Version-$publishMode-Setup.exe" |
    Select-Object -ExpandProperty FullName
