param(
    [string]$Dotnet = 'dotnet',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'),
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$pluginProject = Join-Path $PSScriptRoot 'src/FatePilot/FatePilot.csproj'
$binaryRoot = Join-Path $PSScriptRoot 'src/FatePilot/bin/Release'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

& $Dotnet build $pluginProject -c Release --nologo 2>&1 | Tee-Object -FilePath (Join-Path $outputRoot 'build.log')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
& $Dotnet run --project (Join-Path $PSScriptRoot 'tests/FatePilot.Tests') -c Release -- `
    (Join-Path $PSScriptRoot 'src/FatePilot/Localization') (Join-Path $PSScriptRoot 'src') $binaryRoot `
    2>&1 | Tee-Object -FilePath (Join-Path $outputRoot 'tests.log')
if ($LASTEXITCODE -ne 0) { throw 'Tests or privacy audit failed.' }

$manifest = Get-Content -LiteralPath (Join-Path $binaryRoot 'FatePilot.json') -Raw | ConvertFrom-Json
if ($manifest.Author -ne 'Mokoena' -or $manifest.DalamudApiLevel -ne 15 -or $manifest.AcceptsFeedback -ne $false) { throw 'Unexpected manifest.' }
$version = ([Version]$manifest.AssemblyVersion).ToString(3)
if ($manifest.IconUrl -ne 'https://raw.githubusercontent.com/danil-mokoena/FatePilot/main/src/FatePilot/Assets/icon.png') { throw 'Missing or unexpected installer icon.' }
if ($ValidateOnly) { Write-Output ('Validated without packaging or changing the catalog: ' + $outputRoot); return }
$projectXml = [xml](Get-Content -LiteralPath $pluginProject -Raw)
if ([string]$projectXml.Project.PropertyGroup.Version -match '-dev') { throw 'Development preview: use -ValidateOnly. Do not package or update the catalog until release validation is complete.' }

# Stage only explicitly selected runtime files; never bundle installed dependencies.
$packageRoot = Join-Path $outputRoot "FatePilot-$version"
New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
$runtimeNames = @('FatePilot.dll', 'FatePilot.Core.dll', 'FatePilot.deps.json', 'FatePilot.json')
foreach ($name in $runtimeNames) { Copy-Item -LiteralPath (Join-Path $binaryRoot $name) -Destination (Join-Path $packageRoot $name) -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $packageRoot 'images') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src/FatePilot/Assets/icon.png') -Destination (Join-Path $packageRoot 'images/icon.png') -Force
$docNames = @('README.md', 'SECURITY.md', 'INTEGRATIONS.md', 'VALIDATION.md', 'FALOOP-DESIGN.md')
foreach ($name in $docNames) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $packageRoot $name) -Force }

$pluginZip = Join-Path $outputRoot "FatePilot-$version-plugin.zip"
$sourceZip = Join-Path $outputRoot "FatePilot-$version-source.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Write-TaskZip([string]$Destination, [string]$Root, [string[]]$Files) {
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $Files) {
            $relative = [IO.Path]::GetRelativePath($Root, $file).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose(); $stream.Dispose() }
}
$pluginFiles = @($runtimeNames + $docNames + @('images/icon.png') | ForEach-Object { Join-Path $packageRoot $_ })
Write-TaskZip $pluginZip $packageRoot $pluginFiles
& (Join-Path $PSScriptRoot 'prepare-repository.ps1') -ReleaseDirectory $outputRoot -Tag "v$version"

$sourceFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src'), (Join-Path $PSScriptRoot 'tests') -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | ForEach-Object FullName
    Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object { $_.Extension -in '.md', '.ps1', '.json' -or $_.Name -eq '.gitignore' } | ForEach-Object FullName
)
Write-TaskZip $sourceZip $PSScriptRoot $sourceFiles

$hashLines = foreach ($file in @($pluginZip, $sourceZip) + @($runtimeNames | ForEach-Object { Join-Path $packageRoot $_ })) {
    $hash = Get-FileHash -LiteralPath $file -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash, [IO.Path]::GetRelativePath($outputRoot, $file).Replace('\', '/')
}
$hashLines | Set-Content -LiteralPath (Join-Path $outputRoot 'SHA256SUMS.txt') -Encoding utf8
Write-Output ('Ready: ' + $outputRoot)
