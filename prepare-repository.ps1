param(
    [string]$Owner = 'danil-mokoena',
    [string]$Repository = 'FatePilot',
    [string]$ReleaseDirectory = (Join-Path $PSScriptRoot 'artifacts'),
    [string]$Tag = 'v0.2.1'
)
$ErrorActionPreference = 'Stop'
if ($Owner -notmatch '^[A-Za-z0-9][A-Za-z0-9-]*$' -or $Repository -notmatch '^[A-Za-z0-9_.-]+$' -or $Tag -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+$') { throw 'Invalid repository or tag.' }
$version = $Tag.Substring(1)
$packageRoot = Join-Path $ReleaseDirectory "FatePilot-$version"
$manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'FatePilot.json') -Raw | ConvertFrom-Json
$dllVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $packageRoot 'FatePilot.dll')).Version.ToString()
if ($manifest.AssemblyVersion -ne $dllVersion -or [Version]$dllVersion -ne [Version]"$version.0") { throw 'Release tag, manifest and DLL versions must agree.' }
$zipName = "FatePilot-$version-plugin.zip"
if (!(Test-Path -LiteralPath (Join-Path $ReleaseDirectory $zipName))) { throw 'Run build.ps1 before preparing the catalog.' }
$repoUrl = "https://github.com/$Owner/$Repository"
$downloadUrl = "$repoUrl/releases/download/$Tag/$zipName"
$entry = [ordered]@{
    Author = 'Mokoena'
    Name = 'FatePilot'
    InternalName = 'FatePilot'
    AssemblyVersion = $dllVersion
    Description = $manifest.Description
    Punchline = 'Chart your route. Find your destiny. By Mokoena.'
    RepoUrl = $repoUrl
    IconUrl = $manifest.IconUrl
    ApplicableVersion = 'any'
    DalamudApiLevel = $manifest.DalamudApiLevel
    Tags = @('fate', 'automation', 'experimental')
    IsHide = $false
    IsTestingExclusive = $false
    AcceptsFeedback = $false
    DownloadLinkInstall = $downloadUrl
    DownloadLinkUpdate = $downloadUrl
    DownloadLinkTesting = $downloadUrl
    LastUpdate = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
}
$catalogPath = Join-Path $PSScriptRoot 'pluginmaster.json'
$json = ConvertTo-Json -InputObject @($entry) -Depth 5
[IO.File]::WriteAllText($catalogPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Output ('Prepared catalog: ' + $catalogPath)
Write-Output ('After publication: https://raw.githubusercontent.com/' + $Owner + '/' + $Repository + '/main/pluginmaster.json')
