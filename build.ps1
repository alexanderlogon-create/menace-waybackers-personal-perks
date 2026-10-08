param([string]$GameDir='D:\Steam\steamapps\common\Menace')
$ErrorActionPreference='Stop'
$promiseDotnet=Join-Path $PSScriptRoot '..\MenacePackStudio\.dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $promiseDotnet)) { $promiseDotnet='dotnet' }
& $promiseDotnet build (Join-Path $PSScriptRoot 'src') -c Release --nologo "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& $promiseDotnet run --project (Join-Path $PSScriptRoot 'tests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Rules tests failed.' }
