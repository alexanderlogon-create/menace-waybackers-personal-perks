param([string]$GameDir='D:\Steam\steamapps\common\Menace')
$ErrorActionPreference='Stop'
if (Get-Process Menace -ErrorAction SilentlyContinue) { throw 'Close MENACE before installation.' }
$perkGame=[IO.Path]::GetFullPath($GameDir)
if (!(Test-Path -LiteralPath (Join-Path $perkGame 'Menace.exe'))) { throw 'MENACE installation not found.' }
$perkMods=Join-Path $perkGame 'Mods'
$perkDll=Join-Path $PSScriptRoot 'Mods\MenaceSnipersPromise.dll'
$perkAssets=Join-Path $PSScriptRoot 'Mods\MenaceSnipersPromise'
if (!(Test-Path -LiteralPath $perkDll)) { $perkDll=Join-Path $PSScriptRoot 'src\bin\Release\net6.0\MenaceSnipersPromise.dll'; $perkAssets=Join-Path $PSScriptRoot 'assets' }
$perkManifest=Get-Content -Raw -LiteralPath (Join-Path $perkAssets 'waybackers-perks.json') | ConvertFrom-Json
if ($perkManifest.Count -ne 21) { throw 'Expected 21 new initial perks plus Clover.' }
$perkFiles=@('snipers-promise.png','dublin-rooftops.png','waybackers-perks.json')+@($perkManifest | ForEach-Object { 'waybackers\'+$_.icon })
if (!(Test-Path -LiteralPath $perkDll)) { throw 'Build DLL is missing.' }
foreach($perkFile in $perkFiles) { if (!(Test-Path -LiteralPath (Join-Path $perkAssets $perkFile))) { throw ('Asset is missing: '+$perkFile) } }
$perkBackup=Join-Path $PSScriptRoot ('backups\install-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
foreach ($perkRelative in @('MenaceSnipersPromise.dll','MenaceSnipersPromise')) {
    $perkExisting=Join-Path $perkMods $perkRelative
    if (Test-Path -LiteralPath $perkExisting) {
        New-Item -ItemType Directory -Force $perkBackup | Out-Null
        Copy-Item -LiteralPath $perkExisting -Destination (Join-Path $perkBackup $perkRelative) -Recurse
    }
}
New-Item -ItemType Directory -Force (Join-Path $perkMods 'MenaceSnipersPromise\waybackers') | Out-Null
Copy-Item -LiteralPath $perkDll -Destination (Join-Path $perkMods 'MenaceSnipersPromise.dll') -Force
foreach($perkFile in $perkFiles) { Copy-Item -LiteralPath (Join-Path $perkAssets $perkFile) -Destination (Join-Path $perkMods ('MenaceSnipersPromise\'+$perkFile)) -Force }
Write-Output "Installed Waybackers Personal Perks 0.3.0: 22 initial perks and Dublin Rooftops into $perkGame"
