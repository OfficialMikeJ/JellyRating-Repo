<#
.SYNOPSIS
  Builds the plugin, creates the distribution ZIP and generates the Jellyfin
  repository manifest.json with checksum.

.PARAMETER SourceUrl
  The public URL where the produced ZIP will be downloadable (e.g. the raw
  Gitea URL). Required to produce a valid manifest.json.

.EXAMPLE
  .\package.ps1 -SourceUrl "https://gitea.example.com/plugins/raw/branch/main/dist/Jellyfin.Plugin.ParentalRatingManager_1.0.0.0.zip"
#>
[CmdletBinding()]
param(
    [string]$SourceUrl = "https://gitea.example.com/replace-me/Jellyfin.Plugin.ParentalRatingManager_1.0.0.0.zip",
    [string]$Owner = "YourName",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = 'Stop'
$root      = Split-Path -Parent $MyInvocation.MyCommand.Path
$project   = Join-Path $root 'Jellyfin.Plugin.ParentalRatingManager\Jellyfin.Plugin.ParentalRatingManager.csproj'
$distDir   = Join-Path $root 'dist'
$buildYaml = Join-Path $root 'build.yaml'

# --- read version + guid from build.yaml ---
$yaml = Get-Content $buildYaml -Raw
$version  = [regex]::Match($yaml, 'version:\s*"([^"]+)"').Groups[1].Value
$guid     = [regex]::Match($yaml, 'guid:\s*"([^"]+)"').Groups[1].Value
$targetAbi= [regex]::Match($yaml, 'targetAbi:\s*"([^"]+)"').Groups[1].Value
$name     = [regex]::Match($yaml, 'name:\s*"([^"]+)"').Groups[1].Value
$overview = [regex]::Match($yaml, 'overview:\s*"([^"]+)"').Groups[1].Value
$descr    = [regex]::Match($yaml, 'description:\s*"([^"]+)"').Groups[1].Value
$category = [regex]::Match($yaml, 'category:\s*"([^"]+)"').Groups[1].Value
if (-not $version) { throw 'Could not read version from build.yaml' }

$zipName = "Jellyfin.Plugin.ParentalRatingManager_$version.zip"

Write-Host "==> Building $name v$version ($Configuration)"
dotnet build $project -c $Configuration | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$dllPath = Join-Path $root "Jellyfin.Plugin.ParentalRatingManager\bin\$Configuration\net10.0\Jellyfin.Plugin.ParentalRatingManager.dll"
if (-not (Test-Path $dllPath)) { throw "Built assembly not found at $dllPath" }

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$zipPath = Join-Path $distDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Write-Host "==> Creating $zipName"
Compress-Archive -Path $dllPath -DestinationPath $zipPath -Force

Write-Host "==> Computing MD5 checksum"
$checksum = (Get-FileHash -Path $zipPath -Algorithm MD5).Hash.ToLowerInvariant()
$timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

$manifest = @(
    [ordered]@{
        guid        = $guid
        name        = $name
        description = $descr
        overview    = $overview
        owner       = $Owner
        category    = $category
        versions    = @(
            [ordered]@{
                version   = $version
                changelog = 'Initial release'
                targetAbi = $targetAbi
                sourceUrl = $SourceUrl
                checksum  = $checksum
                timestamp = $timestamp
            }
        )
    }
)

$manifestPath = Join-Path $root 'manifest.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host ''
Write-Host "Done:"
Write-Host "  ZIP       : $zipPath"
Write-Host "  Checksum  : $checksum"
Write-Host "  Manifest  : $manifestPath"
Write-Host ''
Write-Host 'Host manifest.json + the ZIP on your repository (e.g. Gitea raw URLs),'
Write-Host 'then add the manifest.json URL under Dashboard -> Plugins -> Repositories.'
