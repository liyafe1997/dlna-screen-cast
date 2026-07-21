<#
.SYNOPSIS
Builds the x64 MSIX package. Thin wrapper over build-msix.ps1; parameters are
forwarded by name (defaults live in build-msix.ps1).
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$Publisher,
    [string]$PublisherDisplayName,
    [string]$Configuration,
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [switch]$SkipPublish,
    [switch]$NoUnsignedMarker
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-msix.ps1') -Architecture x64 @PSBoundParameters
