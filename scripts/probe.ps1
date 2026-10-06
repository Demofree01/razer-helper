$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $taskRoot 'artifacts\probe.exe'
New-Item -ItemType Directory -Path (Split-Path $output -Parent) -Force | Out-Null
& $compiler /nologo /target:exe /platform:x64 /out:$output /r:System.Web.Extensions.dll (Join-Path $taskRoot 'src\Native.cs') (Join-Path $taskRoot 'src\HidTransport.cs') (Join-Path $PSScriptRoot 'Probe.cs')
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Probe failed' }
