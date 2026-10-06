param([switch]$Tests)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework compiler is unavailable.' }
$dist = Join-Path $taskRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$sources = @(Get-ChildItem (Join-Path $taskRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$refs = @('/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Xml.Linq.dll','/r:System.ServiceProcess.dll')
$common = @('/nologo','/optimize+','/platform:x64','/codepage:65001',('/win32manifest:' + (Join-Path $taskRoot 'app.manifest'))) + $refs
& $compiler @common /target:winexe ('/out:' + (Join-Path $dist 'RazerHelper.exe')) @sources
if ($LASTEXITCODE -ne 0) { throw 'GUI build failed.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'RazerHelper.exe.config') -Destination $dist -Force
& $compiler @common /target:exe ('/out:' + (Join-Path $dist 'RazerHelper.Cli.exe')) @sources
if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'RazerHelper.exe.config') -Destination (Join-Path $dist 'RazerHelper.Cli.exe.config') -Force
if ($Tests) {
    $taskTestSources = @(Get-ChildItem (Join-Path $taskRoot 'tests') -Filter '*.cs' | ForEach-Object { $_.FullName })
    & $compiler @common /target:exe /main:RazerHelper.Tests ('/out:' + (Join-Path $dist 'RazerHelper.Tests.exe')) @sources @taskTestSources
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & (Join-Path $dist 'RazerHelper.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
Get-Item (Join-Path $dist 'RazerHelper.exe') | Select-Object FullName,Length
