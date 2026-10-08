param([string]$GameDir='D:\Steam\steamapps\common\Menace')
$ErrorActionPreference='Stop'
if (Get-Process Menace -ErrorAction SilentlyContinue) { throw 'Close MENACE before removing this mod.' }
$promiseMods=[IO.Path]::GetFullPath((Join-Path $GameDir 'Mods'))
$promiseArchive=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('backups\uninstalled-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))))
foreach ($promiseName in @('MenaceSnipersPromise.dll','MenaceSnipersPromise')) {
    $promiseTarget=[IO.Path]::GetFullPath((Join-Path $promiseMods $promiseName))
    if (!$promiseTarget.StartsWith($promiseMods+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe mod path.' }
    if (Test-Path -LiteralPath $promiseTarget) {
        $promiseResolved=(Resolve-Path -LiteralPath $promiseTarget).Path
        if ($promiseResolved -ne $promiseTarget) { throw 'Unexpected resolved mod path.' }
        New-Item -ItemType Directory -Force $promiseArchive | Out-Null
        Move-Item -LiteralPath $promiseTarget -Destination (Join-Path $promiseArchive $promiseName)
    }
}
Write-Output "Mod files archived to $promiseArchive. Saved Waybackers initial perks fall back to their original perks; learned Dublin Rooftops falls back to Steady Gun."
