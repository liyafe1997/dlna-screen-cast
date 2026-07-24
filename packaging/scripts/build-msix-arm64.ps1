<#
.SYNOPSIS
Builds the arm64 MSIX package. Thin wrapper over build-msix.ps1; parameters
are forwarded by name (defaults live in build-msix.ps1).
Requires the arm64 native runtime (see docs/native-build.md).
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$PackageIdentityName,
    [string]$Publisher,
    [string]$PublisherDisplayName,
    [string]$Configuration,
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [switch]$SkipPublish,
    [switch]$NoUnsignedMarker
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-msix.ps1') -Architecture arm64 @PSBoundParameters
