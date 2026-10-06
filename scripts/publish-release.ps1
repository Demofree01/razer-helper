# Runs only on GitHub's runner. No personal token or local credential is needed.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
    throw 'Release publication is available only in the GitHub Actions workflow.'
}
if ($env:GITHUB_REPOSITORY -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or
    $env:GITHUB_SHA -notmatch '^[0-9a-f]{40}$') { throw 'Invalid repository or commit.' }

$taskRoot = Split-Path $PSScriptRoot -Parent
$taskVersion = (([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $taskRoot 'dist/RazerHelper.exe')).FileVersion -split '\.')[0..2] -join '.')
if ($taskVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid application version.' }
$taskBundleName = 'RazerHelper-' + $taskVersion + '-win-x64'
$taskZipPath = Join-Path $taskRoot ('release/' + $taskBundleName + '.zip')
$taskNotesPath = Join-Path $taskRoot ('docs/releases/' + $taskVersion + '.md')
if (-not (Test-Path -LiteralPath $taskZipPath) -or -not (Test-Path -LiteralPath $taskNotesPath)) {
    throw 'The bundle or version-specific bilingual release notes are missing.'
}

# Refuse an unexpected archive rather than risk publishing local data.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskAllowedFiles = @('RazerHelper.exe','RazerHelper.exe.config','README.md','README.en.md','LICENSE','SHA256SUMS.txt',
    'docs/PROTOCOL.md','docs/VALIDATION.md','docs/PERFORMANCE.md','docs/ADVANCED.md','docs/MACROS.md')
$taskArchive = [IO.Compression.ZipFile]::OpenRead($taskZipPath)
try {
    $taskEntries = @($taskArchive.Entries)
    if ($taskEntries.Count -ne $taskAllowedFiles.Count) { throw 'Unexpected ZIP file count.' }
    foreach ($taskAllowedFile in $taskAllowedFiles) {
        $taskMatches = @($taskEntries | Where-Object { $_.FullName.Replace('\','/') -eq ($taskBundleName + '/' + $taskAllowedFile) })
        if ($taskMatches.Count -ne 1 -or $taskMatches[0].Length -eq 0) { throw ('Missing or invalid ZIP entry: ' + $taskAllowedFile) }
    }
} finally { $taskArchive.Dispose() }

$taskZipHash = (Get-FileHash -LiteralPath $taskZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$taskChecksumPath = Join-Path $taskRoot ('release/SHA256SUMS-' + $taskVersion + '-win-x64.txt')
[IO.File]::WriteAllText($taskChecksumPath, $taskZipHash + '  ' + $taskBundleName + '.zip' + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))

$taskApiBase = 'https://api.github.com/repos/' + $env:GITHUB_REPOSITORY
$taskTag = 'v' + $taskVersion
$taskHeaders = @{
    Authorization = 'Bearer ' + $env:GITHUB_TOKEN
    Accept = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2026-03-10'
}
$taskExisting = $null
try { $taskExisting = Invoke-RestMethod -Uri ($taskApiBase + '/releases/tags/' + $taskTag) -Headers $taskHeaders }
catch { if ([int]$_.Exception.Response.StatusCode -ne 404) { throw } }
if ($null -ne $taskExisting) {
    $taskNames = @($taskExisting.assets | Where-Object { $_.state -eq 'uploaded' -and $_.size -gt 0 } | ForEach-Object { $_.name })
    if (-not $taskExisting.draft -and $taskNames -contains ($taskBundleName + '.zip') -and
        $taskNames -contains ([IO.Path]::GetFileName($taskChecksumPath))) {
        # Documentation edits may update notes while preserving the published assets.
        $taskCurrentNotes = [IO.File]::ReadAllText($taskNotesPath)
        if ($taskExisting.body.Replace("`r`n","`n").Trim() -cne $taskCurrentNotes.Replace("`r`n","`n").Trim()) {
            $taskNotesUpdate = @{body=$taskCurrentNotes} | ConvertTo-Json
            Invoke-RestMethod -Method Patch -Uri ($taskApiBase + '/releases/' + $taskExisting.id) -Headers $taskHeaders -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($taskNotesUpdate)) | Out-Null
            Write-Host 'Updated bilingual release notes.'
        }
        Write-Host ('Already published: ' + $taskExisting.html_url)
        return
    }
    throw 'This version already has an incomplete release. Inspect it before retrying; existing assets will not be overwritten.'
}

$taskCreate = @{
    tag_name = $taskTag
    target_commitish = $env:GITHUB_SHA
    name = 'Razer Helper ' + $taskVersion
    body = [IO.File]::ReadAllText($taskNotesPath)
    draft = $true
    prerelease = $true
    make_latest = 'false'
} | ConvertTo-Json -Depth 5
$taskRelease = Invoke-RestMethod -Method Post -Uri ($taskApiBase + '/releases') -Headers $taskHeaders -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($taskCreate))
$taskUploadBase = ($taskRelease.upload_url -split '\{')[0]
if (-not $taskUploadBase.StartsWith('https://uploads.github.com/repos/' + $env:GITHUB_REPOSITORY + '/releases/', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Unexpected upload destination.'
}
foreach ($taskUploadPath in @($taskZipPath, $taskChecksumPath)) {
    $taskUploadName = [IO.Path]::GetFileName($taskUploadPath)
    $taskContentType = if ($taskUploadName.EndsWith('.zip')) { 'application/zip' } else { 'text/plain' }
    $taskAsset = Invoke-RestMethod -Method Post -Uri ($taskUploadBase + '?name=' + [Uri]::EscapeDataString($taskUploadName)) -Headers $taskHeaders -ContentType $taskContentType -InFile $taskUploadPath
    $taskExpectedHash = 'sha256:' + (Get-FileHash -LiteralPath $taskUploadPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($taskAsset.state -ne 'uploaded' -or $taskAsset.size -ne (Get-Item -LiteralPath $taskUploadPath).Length -or
        ($taskAsset.digest -and $taskAsset.digest -cne $taskExpectedHash)) { throw ('Asset verification failed: ' + $taskUploadName) }
}
$taskPublished = Invoke-RestMethod -Method Patch -Uri ($taskApiBase + '/releases/' + $taskRelease.id) -Headers $taskHeaders -ContentType 'application/json' -Body '{"draft":false}'
if ($taskPublished.draft) { throw 'Release remained a draft.' }
Write-Host ('Published: ' + $taskPublished.html_url)
Write-Host ('ZIP SHA256: ' + $taskZipHash)
