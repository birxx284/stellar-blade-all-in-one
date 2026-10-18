$ErrorActionPreference = 'Stop'
$unifiedRoot = $PSScriptRoot
$unifiedCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$unifiedDist = Join-Path $unifiedRoot 'dist'
New-Item -ItemType Directory -Path $unifiedDist -Force | Out-Null
$unifiedOutput = Join-Path $unifiedDist 'StellarBlade-AllInOne.exe'
$unifiedSources = Get-ChildItem -LiteralPath (Join-Path $unifiedRoot 'src') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName
$unifiedCatalog = Join-Path $unifiedRoot 'src\wardrobe.tsv'
$unifiedManifest = Join-Path $unifiedRoot 'src\app.manifest'
& $unifiedCompiler /nologo /target:winexe /platform:x64 /optimize+ /main:StellarUnified.Program "/out:$unifiedOutput" "/win32manifest:$unifiedManifest" "/resource:$unifiedCatalog,wardrobe.tsv" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $unifiedSources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output $unifiedOutput
