[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'stage'

$manifest = Get-Content (Join-Path $root 'extension.yaml') -Raw
$versionMatch = [regex]::Match($manifest, '(?m)^Version:\s*(.+)$')
if (-not $versionMatch.Success) { throw 'No se pudo leer la version de extension.yaml.' }
$version = $versionMatch.Groups[1].Value.Trim()

if (-not $SkipBuild) {
    dotnet build (Join-Path $root 'CBServersPlaynite.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'La compilacion ha fallado.' }
}

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item $stage -ItemType Directory | Out-Null

Copy-Item (Join-Path $root 'bin\CBServersPlaynite.dll') $stage
Copy-Item (Join-Path $root 'extension.yaml') $stage
Copy-Item (Join-Path $root 'Assets') $stage -Recurse

$package = Join-Path $dist ("CBServersPlaynite_{0}.pext" -f $version)
$archive = "$package.zip"
foreach ($file in @($package, $archive)) {
    if (Test-Path $file) { Remove-Item $file -Force }
}

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -CompressionLevel Optimal
Move-Item $archive $package
Remove-Item $stage -Recurse -Force

Write-Host "Paquete creado: $package"
