<#
.SYNOPSIS
Builds, packs, bundles and signs the DLNA Screen Cast MSIX package.

.DESCRIPTION
Pipeline: dotnet publish (self-contained, includes .NET Desktop Runtime +
ASP.NET Core shared framework) -> repair publish gaps (MRT resources.pri,
app-local VC++ CRT) -> makeappx pack -> makeappx bundle.

Packages are UNSIGNED by default (install with Developer Mode +
Add-AppxPackage -AllowUnsigned, or sign afterwards). Pass -CertificatePath
to sign with a real certificate; its subject must match -Publisher.

The generated AppxManifest declares a windows.firewallRules extension so the
installer registers inbound TCP/UDP allow rules for the exe with Profile="all".

Prerequisites: the native media runtime must already be built (see
docs/native-build.md); dotnet SDK per global.json; makeappx/signtool from the
Windows SDK or the Microsoft.Windows.SDK.BuildTools NuGet package (restored
automatically by the solution).

.EXAMPLE
# Unsigned x64 package (default):
pwsh packaging/scripts/build-msix.ps1 -Version 1.0.0.0

.EXAMPLE
# Unsigned arm64 package (requires the arm64 native runtime, see
# docs/native-build.md):
pwsh packaging/scripts/build-msix.ps1 -Version 1.0.0.0 -Architecture arm64

.EXAMPLE
# Signed package:
packaging/scripts/build-msix.ps1 -Version 1.2.0.0 `
  -Publisher 'CN=Contoso Ltd, O=Contoso Ltd, C=SE' `
  -CertificatePath C:\secrets\contoso.pfx -CertificatePassword '...'
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0.0',
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture = 'x64',
    [string]$Publisher = 'CN=DLNAScreenCast Dev',
    [string]$PublisherDisplayName = 'DLNA Screen Cast Project',
    [string]$Configuration = 'Release',
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [switch]$SkipPublish,
    # Store submissions are unsigned but must NOT carry the unsigned-package
    # marker OID; the Store signs the package itself.
    [switch]$NoUnsignedMarker
)

$ErrorActionPreference = 'Stop'
$architecture = $Architecture
$rid = "win-$architecture"

# Unsigned packages can only be installed (Add-AppxPackage -AllowUnsigned)
# when the Identity Publisher carries the "unsigned package" marker OID, which
# prevents an unsigned package from claiming a signed publisher identity.
# https://learn.microsoft.com/windows/msix/package/unsigned-package
$unsignedMarker = 'OID.2.25.311729368913984317654407730594956997722=1'
if (-not $CertificatePath -and -not $NoUnsignedMarker -and $Publisher -notlike "*$unsignedMarker*") {
    $Publisher = "$Publisher, $unsignedMarker"
    Write-Host "Unsigned build: appended unsigned-package marker OID to Publisher."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$appProject = Join-Path $repoRoot 'src\DesktopDlnaCast.App\DesktopDlnaCast.App.csproj'
$msixRoot = Join-Path $repoRoot 'out\msix'
$publishDir = Join-Path $msixRoot "publish\$architecture"
$layoutDir = Join-Path $msixRoot "layout\$architecture"
$artifactDir = Join-Path $msixRoot 'artifacts'
$templatePath = Join-Path $repoRoot 'packaging\msix\AppxManifest.template.xml'
$assetsDir = Join-Path $repoRoot 'packaging\msix\Assets'

function Find-SdkTool([string]$name) {
    $candidates = @()
    $kitsRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path $kitsRoot) {
        $candidates += Get-ChildItem "$kitsRoot\*\x64\$name" -ErrorAction SilentlyContinue
    }
    $nugetRoot = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools'
    if (Test-Path $nugetRoot) {
        $candidates += Get-ChildItem "$nugetRoot\*\bin\*\x64\$name" -ErrorAction SilentlyContinue
    }
    $tool = $candidates | Sort-Object FullName | Select-Object -Last 1
    if (-not $tool) {
        throw "$name not found. Install the Windows SDK or restore the solution so Microsoft.Windows.SDK.BuildTools is in the NuGet cache."
    }
    $tool.FullName
}

function Invoke-Checked([string]$exe, [string[]]$exeArgs, [string]$what) {
    & $exe @exeArgs
    if ($LASTEXITCODE -ne 0) { throw "$what failed with exit code $LASTEXITCODE." }
}

$makeappx = Find-SdkTool 'makeappx.exe'
$signtool = Find-SdkTool 'signtool.exe'
Write-Host "makeappx: $makeappx"
Write-Host "signtool: $signtool"

# 1. Publish: self-contained so the .NET Desktop Runtime, the ASP.NET Core
#    shared framework (Kestrel) and the Windows App SDK runtime all ship inside
#    the package. MSIX cannot chain runtime installers and there is no MSIX
#    framework package for ASP.NET Core, so self-contained is the only layout
#    that installs offline and never breaks on a missing machine-wide runtime.
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

# 3. Assemble the package layout.
Write-Host '== Assembling package layout =='
robocopy $publishDir $layoutDir /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy publish->layout failed with exit code $LASTEXITCODE." }

# 3a. MRT resources: dotnet publish does not copy the generated PRI for
#     unpackaged-configured WinUI projects, but the packaged app needs it for
#     every localized string. Take it from the build output and install it under
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

# 3c. Manifest + logo assets.
New-Item -ItemType Directory -Force (Join-Path $layoutDir 'Assets') | Out-Null
Copy-Item (Join-Path $assetsDir '*.png') (Join-Path $layoutDir 'Assets') -Force
$manifest = Get-Content $templatePath -Raw -Encoding utf8
$manifest = $manifest.Replace('$VERSION$', $Version)
$manifest = $manifest.Replace('$PUBLISHER$', $Publisher)
$manifest = $manifest.Replace('$PUBLISHER_DISPLAY_NAME$', $PublisherDisplayName)
$manifest = $manifest.Replace('$ARCHITECTURE$', $architecture)
Set-Content (Join-Path $layoutDir 'AppxManifest.xml') $manifest -Encoding utf8

# 4. Pack.
New-Item -ItemType Directory -Force $artifactDir | Out-Null
$msixPath = Join-Path $artifactDir "DLNAScreenCast_${Version}_$architecture.msix"
Write-Host "== Packing $msixPath =="
Invoke-Checked $makeappx @('pack', '/o', '/d', $layoutDir, '/p', $msixPath) 'makeappx pack'

# 5. Signing: only when a certificate is supplied; the default output is an
#    unsigned package.
$signArgs = $null
if ($CertificatePath) {
    $signArgs = @('sign', '/fd', 'SHA256', '/f', $CertificatePath)
    if ($CertificatePassword) { $signArgs += @('/p', $CertificatePassword) }
    Write-Host '== Signing package =='
    Invoke-Checked $signtool ($signArgs + @($msixPath)) 'signtool sign (msix)'
}

# 6. Bundle (single-architecture bundle; the file name carries the
#    architecture so x64 and arm64 artifacts can coexist).
$bundleInput = Join-Path $msixRoot "bundle-input\$architecture"
New-Item -ItemType Directory -Force $bundleInput | Out-Null
Get-ChildItem $bundleInput -File | Remove-Item -Force -Confirm:$false
Copy-Item $msixPath $bundleInput
$bundlePath = Join-Path $artifactDir "DLNAScreenCast_${Version}_$architecture.msixbundle"
Write-Host "== Bundling $bundlePath =="
Invoke-Checked $makeappx @('bundle', '/o', '/bv', $Version, '/d', $bundleInput, '/p', $bundlePath) 'makeappx bundle'
if ($signArgs) {
    Write-Host '== Signing bundle =='
    Invoke-Checked $signtool ($signArgs + @($bundlePath)) 'signtool sign (bundle)'
}

Write-Host ''
Write-Host '== Done =='
foreach ($artifact in @($msixPath, $bundlePath)) {
    $hash = (Get-FileHash $artifact -Algorithm SHA256).Hash
    $size = '{0:N1} MB' -f ((Get-Item $artifact).Length / 1MB)
    Write-Host "$artifact  ($size)"
    Write-Host "  SHA256: $hash"
}
if (-not $signArgs) {
    Write-Host ''
    Write-Host 'Packages are UNSIGNED. Install on Windows 11 from an ADMIN PowerShell:'
    Write-Host "  Add-AppxPackage -Path DLNAScreenCast_${Version}_$architecture.msixbundle -AllowUnsigned"
    Write-Host 'For distribution, sign with a trusted certificate (-CertificatePath) or via the Store.'
}
