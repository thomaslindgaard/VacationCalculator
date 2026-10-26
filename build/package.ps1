<#
.SYNOPSIS
    Builds the portable release zip of Vacation Calculator (docs/features.md, feature 9).

.DESCRIPTION
    Runs all tests (and stops if any fail), publishes the App as a self-contained single-file
    win-x64 exe and zips it into artifacts\VacationCalculator-<version>-win-x64.zip.
    The version is read from src\VacationCalculator.App\VacationCalculator.App.csproj.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\package.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root 'src\VacationCalculator.App\VacationCalculator.App.csproj'
$solution = Join-Path $root 'VacationCalculator.sln'
$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts 'publish\win-x64'

$version = ([xml](Get-Content -Raw $appProject)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> found in $appProject." }
$zip = Join-Path $artifacts "VacationCalculator-$version-win-x64.zip"

Write-Host "Vacation Calculator $version" -ForegroundColor Cyan

Write-Host "`n== Tests ==" -ForegroundColor Cyan
dotnet test $solution -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed; no package was built." }

Write-Host "`n== Publish ==" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
dotnet publish $appProject -c Release -p:PublishProfile=win-x64 -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$files = Get-ChildItem $publishDir -File
if (-not ($files | Where-Object Name -eq 'VacationCalculator.exe')) { throw "VacationCalculator.exe is missing from $publishDir." }

Write-Host "`n== Zip ==" -ForegroundColor Cyan
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path ($files.FullName) -DestinationPath $zip

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "`nPackage: $zip ($sizeMb MB)" -ForegroundColor Green
Write-Host "Contents: $($files.Name -join ', ')"
Write-Host "SHA-256:  $hash"
