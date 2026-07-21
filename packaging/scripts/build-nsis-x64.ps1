<#
.SYNOPSIS
Builds the x64 NSIS setup .exe. Thin wrapper over build-nsis.ps1; parameters
are forwarded by name (defaults live in build-nsis.ps1).
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$Configuration,
    [switch]$SkipPublish
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-nsis.ps1') -Architecture x64 @PSBoundParameters
