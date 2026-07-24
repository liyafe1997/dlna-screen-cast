<#
.SYNOPSIS
Builds the DLNA Screen Cast NSIS setup (classic .exe installer).

.DESCRIPTION
Pipeline: dotnet publish (self-contained, includes .NET Desktop Runtime +
ASP.NET Core shared framework) -> repair publish gaps (MRT resources.pri,
app-local VC++ CRT) -> makensis packaging/nsis/DesktopDlnaCast.nsi.

The publish/repair steps mirror packaging/scripts/build-msix.ps1 (keep the two
in sync). The installer itself registers all-profile inbound firewall rules for
the app and silently uninstalls a previous version on upgrade — see
packaging/nsis/DesktopDlnaCast.nsi.

Prerequisites: the native media runtime must already be built (see
docs/native-build.md); dotnet SDK per global.json; NSIS 3.x (makensis) —
install with: winget install NSIS.NSIS

.EXAMPLE
pwsh packaging/scripts/build-nsis.ps1 -Version 1.2.0.0

.EXAMPLE
# arm64 installer (requires the arm64 native runtime, see docs/native-build.md):
pwsh packaging/scripts/build-nsis.ps1 -Version 1.2.0.0 -Architecture arm64
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version = '1.2.0.0',
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture = 'x64',
    [string]$Configuration = 'Release',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$architecture = $Architecture
$rid = "win-$architecture"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$appProject = Join-Path $repoRoot 'src\DesktopDlnaCast.App\DesktopDlnaCast.App.csproj'
$nsisRoot = Join-Path $repoRoot 'out\nsis'
$publishDir = Join-Path $nsisRoot "publish\$architecture"
$layoutDir = Join-Path $nsisRoot "layout\$architecture"
$artifactDir = Join-Path $nsisRoot 'artifacts'
$nsiScript = Join-Path $repoRoot 'packaging\nsis\DesktopDlnaCast.nsi'

function Find-Makensis {
    $cmd = Get-Command 'makensis.exe' -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($root in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if (-not $root) { continue }
        $candidate = Join-Path $root 'NSIS\makensis.exe'
        if (Test-Path $candidate) { return $candidate }
    }
    throw 'makensis.exe not found. Install NSIS 3.x, e.g.: winget install NSIS.NSIS'
}

function Invoke-Checked([string]$exe, [string[]]$exeArgs, [string]$what) {
    & $exe @exeArgs
    if ($LASTEXITCODE -ne 0) { throw "$what failed with exit code $LASTEXITCODE." }
}

$makensis = Find-Makensis
Write-Host "makensis: $makensis"

# 1. Publish: self-contained so the .NET Desktop Runtime, the ASP.NET Core
#    shared framework (Kestrel) and the Windows App SDK runtime all ship inside
#    the installer and a clean machine needs no runtime installs.
if (-not $SkipPublish) {
    Write-Host "== Publishing $Configuration/$rid (self-contained) =="
    # CopyDesktopDlnaCastNativeRuntime is deliberately NOT passed globally:
    # the app project sets it itself and infers the native architecture from
    # its own RuntimeIdentifier. A global -p: would make every referenced
    # library attach its default-arch (x64) native DLLs too and break the
    # arm64 publish with NETSDK1152 duplicates.
    Invoke-Checked 'dotnet' @(
        'publish', $appProject,
        '-c', $Configuration,
        '-r', $rid,
        '--self-contained', 'true',
        '-o', $publishDir,
        '-p:ValidateDesktopDlnaCastNativeRuntime=true'
    ) 'dotnet publish'
}

# 2. Validate that every runtime layer actually landed in the publish output.
$requiredFiles = @(
    'DLNAScreenCast.exe',
    'appsettings.json',
    'coreclr.dll',                                  # .NET runtime
    'hostfxr.dll',
    'Microsoft.AspNetCore.Server.Kestrel.Core.dll', # ASP.NET Core shared framework
    'Microsoft.ui.xaml.dll',                        # Windows App SDK (self-contained)
    'DesktopDlnaCast.Media.Native.dll'              # native media core
)
foreach ($f in $requiredFiles) {
    if (-not (Test-Path (Join-Path $publishDir $f))) {
        throw "Publish output is missing $f. Did the publish or the native build fail?"
    }
}
foreach ($pattern in @('avcodec-*.dll', 'avformat-*.dll', 'avutil-*.dll', 'swscale-*.dll')) {
    if (-not (Get-ChildItem (Join-Path $publishDir $pattern) -ErrorAction SilentlyContinue)) {
        throw "Publish output is missing FFmpeg runtime '$pattern'. Build the native preset first (docs/native-build.md)."
    }
}

# 3. Assemble the install layout.
Write-Host '== Assembling install layout =='
robocopy $publishDir $layoutDir /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy publish->layout failed with exit code $LASTEXITCODE." }

# 3a. MRT resources: dotnet publish does not copy the generated PRI for
#     unpackaged-configured WinUI projects, but the app needs it for every
#     localized string. Take it from the build output and install it under
#     both probe names.
$appPri = Get-ChildItem (Join-Path $repoRoot "src\DesktopDlnaCast.App\bin\$Configuration\*\$rid\DLNAScreenCast.pri") -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $appPri) {
    throw "DLNAScreenCast.pri not found under bin\$Configuration. Build the app first (it is produced by the normal build)."
}
Copy-Item $appPri.FullName (Join-Path $layoutDir 'resources.pri') -Force
Copy-Item $appPri.FullName (Join-Path $layoutDir 'DLNAScreenCast.pri') -Force

# 3b. App-local VC++ CRT: the native media core and the FFmpeg DLLs link
#     against vcruntime140/msvcp140, which neither the .NET runtime pack nor the
#     self-contained Windows App SDK provides. Ship them app-local so a clean
#     machine needs no VC++ Redistributable install.
#     The copied DLLs MUST match the target architecture: on ARM64 dev machines
#     System32 holds ARM64X binaries that load fine under local x64 emulation
#     but crash on real x64 hardware, and Host*-crossed compiler folders carry
#     host-arch CRTs, so every candidate is verified against its PE header.
function Get-PEMachine([string]$path) {
    $fs = [System.IO.File]::OpenRead($path)
    try {
        $br = New-Object System.IO.BinaryReader($fs)
        $fs.Seek(0x3C, 'Begin') | Out-Null
        $fs.Seek($br.ReadInt32() + 4, 'Begin') | Out-Null
        return $br.ReadUInt16()
    } finally { $fs.Close() }
}
$requiredMachine = if ($architecture -eq 'arm64') { 0xAA64 } else { 0x8664 }
# vcruntime140_1.dll exists only for x64 (exception-handling helper); arm64
# binaries never import it.
$crtNames = @('vcruntime140.dll', 'msvcp140.dll')
if ($architecture -eq 'x64') { $crtNames += 'vcruntime140_1.dll' }
$crtSourceDirs = @()
foreach ($vsRoot in @("$env:ProgramFiles\Microsoft Visual Studio", "${env:ProgramFiles(x86)}\Microsoft Visual Studio")) {
    # Preferred: the redistributable folder; fallback: the compiler toolchain
    # folders targeting $architecture (the natively-hosted one carries the
    # target-arch CRT binaries; the PE check drops cross-hosted copies).
    $crtSourceDirs += Get-ChildItem "$vsRoot\*\*\VC\Redist\MSVC\*\$architecture\Microsoft.VC*.CRT" -Directory -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending
    $crtSourceDirs += Get-ChildItem "$vsRoot\*\*\VC\Tools\MSVC\*\bin\Host*\$architecture" -Directory -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending
}
# Repo-local assembled toolchain (out/tooling) used on machines without a full
# VS C++ workload; harmless no-op elsewhere.
$crtSourceDirs += Get-ChildItem "$repoRoot\out\tooling\*\VC\Tools\MSVC\*\bin\Host*\$architecture" -Directory -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending
$crtSourceDirs += "$env:SystemRoot\System32"
foreach ($name in $crtNames) {
    $target = Join-Path $layoutDir $name
    if ((Test-Path $target) -and (Get-PEMachine $target) -eq $requiredMachine) { continue }
    $source = $crtSourceDirs |
        ForEach-Object { Join-Path $_ $name } |
        Where-Object { (Test-Path $_) -and (Get-PEMachine $_) -eq $requiredMachine } |
        Select-Object -First 1
    if (-not $source) {
        throw "No $architecture $name found (looked in VC Redist, VC Tools Host*\$architecture and System32). Install the VS 'C++ $architecture redistributable' component."
    }
    Copy-Item $source $target -Force
    Write-Host "CRT: $name <- $source"
}

# 4. Compile the installer.
New-Item -ItemType Directory -Force $artifactDir | Out-Null
$setupPath = Join-Path $artifactDir "DLNAScreenCast_${Version}_${architecture}_Setup.exe"
Write-Host "== Compiling $setupPath =="
Invoke-Checked $makensis @(
    '/INPUTCHARSET', 'UTF8',
    "/DVERSION=$Version",
    "/DARCHITECTURE=$architecture",
    "/DLAYOUT_DIR=$layoutDir",
    "/DOUTFILE=$setupPath",
    $nsiScript
) 'makensis'

Write-Host ''
Write-Host '== Done =='
$hash = (Get-FileHash $setupPath -Algorithm SHA256).Hash
$size = '{0:N1} MB' -f ((Get-Item $setupPath).Length / 1MB)
Write-Host "$setupPath  ($size)"
Write-Host "  SHA256: $hash"
Write-Host ''
Write-Host 'The installer is UNSIGNED; SmartScreen may warn on first run.'
Write-Host 'It registers all-profile inbound firewall rules for the app and'
Write-Host 'silently uninstalls a previous version when upgrading.'
