param([string]$GameDir='D:\Steam\steamapps\common\Menace')
$ErrorActionPreference='Stop'
if (Get-Process Menace -ErrorAction SilentlyContinue) { throw 'Close MENACE before verification.' }
& (Join-Path $PSScriptRoot 'install.ps1') -GameDir $GameDir
$promiseOutput=Join-Path $PSScriptRoot 'artifacts\runtime'
if (Test-Path -LiteralPath $promiseOutput) {
    $promiseResolved=(Resolve-Path -LiteralPath $promiseOutput).Path
    $promiseExpected=[IO.Path]::GetFullPath($promiseOutput)
    if ($promiseResolved -ne $promiseExpected -or !$promiseExpected.StartsWith([IO.Path]::GetFullPath($PSScriptRoot)+'\')) { throw 'Unsafe verification path.' }
    Move-Item -LiteralPath $promiseResolved -Destination ($promiseExpected+'-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
}
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'artifacts') | Out-Null
$env:SteamAppId='2432860'
$env:SteamGameId='2432860'
$env:MENACE_PROMISE_VERIFY_OUTPUT=$promiseOutput
$promiseLog=(Join-Path $PSScriptRoot 'artifacts\game-verification.log').Replace('\','/')
# The test flag activates process-local audio muting before game startup sounds.
$promiseProcess=Start-Process -FilePath (Join-Path $GameDir 'Menace.exe') -WorkingDirectory $GameDir -ArgumentList '--snipers-promise-test','-batchmode','--melonloader.hideconsole','--melonloader.disablestartscreen','-logFile',$promiseLog -WindowStyle Hidden -PassThru
$promiseDeadline=[DateTime]::UtcNow.AddMinutes(3)
while (!$promiseProcess.HasExited -and [DateTime]::UtcNow -lt $promiseDeadline) {
    $promiseProcess.WaitForExit(1000) | Out-Null
    $promiseProcess.Refresh()
}
if (!$promiseProcess.HasExited) { Stop-Process -Id $promiseProcess.Id; throw 'Isolated runtime verification timed out.' }
if (Test-Path -LiteralPath (Join-Path $promiseOutput 'error.txt')) { throw (Get-Content -Raw -LiteralPath (Join-Path $promiseOutput 'error.txt')) }
$promiseResult=Get-Content -Raw -LiteralPath (Join-Path $promiseOutput 'verification.json') | ConvertFrom-Json
if (!$promiseResult.success) { throw 'Runtime verification failed.' }
if (!$promiseResult.audioMuted) { throw 'Runtime verification did not confirm muted audio.' }
$promiseResult
