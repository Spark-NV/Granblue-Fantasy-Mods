$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$modId = 'gbfr.spark.granblue-fantasy-mods'
$version = (Get-Content (Join-Path $root 'mod/ModConfig.json') -Raw | ConvertFrom-Json).ModVersion
$packageRoot = Join-Path $root 'package'
$package = Join-Path $packageRoot $modId

if (Test-Path $packageRoot) { Remove-Item $packageRoot -Recurse -Force }
dotnet build (Join-Path $root 'src/GranblueFantasyMods.csproj') --configuration Release --output $package
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }
Copy-Item (Join-Path $root 'mod/*') $package
Copy-Item (Join-Path $root 'THIRD_PARTY_NOTICES.md') $package

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist "$modId-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $package -DestinationPath $zip

Write-Host "Built $package"
Write-Host "Release archive: $zip"
