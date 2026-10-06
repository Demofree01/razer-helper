$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$release = Join-Path $taskRoot 'release'
$taskExePath = Join-Path $taskRoot 'dist\RazerHelper.exe'
$taskVersion = (([Diagnostics.FileVersionInfo]::GetVersionInfo($taskExePath).FileVersion -split '\.')[0..2] -join '.')
$taskBundleName = 'RazerHelper-' + $taskVersion + '-win-x64'
$bundle = Join-Path $release $taskBundleName
New-Item -ItemType Directory -Path $bundle -Force | Out-Null
foreach ($taskFile in @('RazerHelper.exe','RazerHelper.exe.config')) {
    $taskPackageSource = Join-Path $taskRoot ('dist\' + $taskFile)
    $taskPackageTarget = Join-Path $bundle $taskFile
    # The running portable executable is locked. Identical files need no copy.
    if ((Test-Path -LiteralPath $taskPackageTarget) -and
        (Get-FileHash -LiteralPath $taskPackageSource -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $taskPackageTarget -Algorithm SHA256).Hash) { continue }
    Copy-Item -LiteralPath $taskPackageSource -Destination $bundle -Force
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md'),(Join-Path $taskRoot 'README.en.md'),(Join-Path $taskRoot 'LICENSE') -Destination $bundle -Force
$bundleDocs = Join-Path $bundle 'docs'
New-Item -ItemType Directory -Path $bundleDocs -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\PROTOCOL.md'),(Join-Path $taskRoot 'docs\VALIDATION.md'),(Join-Path $taskRoot 'docs\PERFORMANCE.md'),(Join-Path $taskRoot 'docs\ADVANCED.md'),(Join-Path $taskRoot 'docs\MACROS.md') -Destination $bundleDocs -Force
$hash = (Get-FileHash -LiteralPath (Join-Path $bundle 'RazerHelper.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $bundle 'SHA256SUMS.txt'), $hash + '  RazerHelper.exe' + [Environment]::NewLine)
Compress-Archive -LiteralPath $bundle -DestinationPath (Join-Path $release ($taskBundleName + '.zip')) -Force
Get-FileHash -LiteralPath (Join-Path $release ($taskBundleName + '.zip')) -Algorithm SHA256
