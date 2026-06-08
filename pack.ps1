<#
.SYNOPSIS
  Build the framework-dependent release artifact for sercat and report its SHA256.

.DESCRIPTION
  Publishes a framework-dependent win-x64 build (no PDB), zips the publish output, and
  prints the SHA256. Upload the resulting zip as a GitHub Release asset; the printed hash
  is what goes into the winget installer manifest (InstallerSha256). Because a GitHub
  release serves the exact bytes uploaded, the local hash matches the served one.

.EXAMPLE
  pwsh ./pack.ps1 -Version 1.0.0
#>
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$pub  = Join-Path $root "bin\Release\net8.0\win-x64\publish"
$zip  = Join-Path $root "sercat-$Version-win-x64.zip"

Write-Host "Publishing framework-dependent win-x64 build..."
dotnet publish (Join-Path $root "sercat.csproj") `
    -c Release -r win-x64 --self-contained false `
    -p:Version=$Version -p:DebugType=none -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Belt-and-suspenders: drop any stray PDB so it never ships.
Get-ChildItem $pub -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $pub "*") -DestinationPath $zip

$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
$size = [math]::Round((Get-Item -LiteralPath $zip).Length / 1KB, 1)

Write-Host ""
Write-Host "Artifact : $zip"
Write-Host "Size     : $size KB"
Write-Host "SHA256   : $hash"
Write-Host ""
Write-Host "Contents:"
Get-ChildItem $pub | ForEach-Object { Write-Host ("  " + $_.Name) }
