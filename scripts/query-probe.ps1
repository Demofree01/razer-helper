$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $taskRoot 'artifacts\query-probe.exe'
& $compiler /nologo /target:exe /platform:x64 /out:$output (Join-Path $taskRoot 'src\Native.cs') (Join-Path $taskRoot 'src\HidTransport.cs') (Join-Path $PSScriptRoot 'QueryProbe.cs')
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Probe failed' }
