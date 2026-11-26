[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'HanLin-ToDo'),
    [string]$DotNet = 'dotnet',
    [string]$StagingDirectory,
    [switch]$BuildOnly,
    [switch]$SkipLaunch
)

$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\HanLin.ToDo\HanLin.ToDo.csproj'))
if (-not $StagingDirectory) {
    $StagingDirectory = Join-Path (Split-Path -Parent $project) 'obj\windows-publish'
}
$stagingPath = [IO.Path]::GetFullPath($StagingDirectory)
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if ($stagingPath.TrimEnd('\', '/') -eq $outputPath.TrimEnd('\', '/')) {
    throw 'Use a staging directory separate from the installed executable.'
}

$publishArgs = @(
    'publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true'
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true'
    '-p:EnableCompressionInSingleFile=true', '-p:DebugType=none', '-p:DebugSymbols=false'
    '-warnaserror', '--nologo', '-o', $stagingPath
)
& $DotNet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$packedExe = Join-Path $stagingPath 'HanLin-ToDo.exe'
if ($BuildOnly) {
    Get-Item -LiteralPath $packedExe
    return
}

$processName = 'HanLin-ToDo'
$running = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt 40; $i++) {
        if (@(Get-Process -Name $processName -ErrorAction SilentlyContinue).Count -eq 0) { break }
        Start-Sleep -Milliseconds 250
    }
}

$packedHash = (Get-FileHash -LiteralPath $packedExe -Algorithm SHA256).Hash
[void][IO.Directory]::CreateDirectory($outputPath)
$destination = Join-Path $outputPath 'HanLin-ToDo.exe'
$tempExe = Join-Path $outputPath ('.hanlin-publish-' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    [IO.File]::Copy($packedExe, $tempExe)
    if ((Get-FileHash -LiteralPath $tempExe -Algorithm SHA256).Hash -ne $packedHash) {
        throw 'The staged executable checksum does not match the build.'
    }
    if ([IO.File]::Exists($destination)) {
        try {
            [IO.File]::Replace($tempExe, $destination, [NullString]::Value)
        }
        catch [IO.IOException], [UnauthorizedAccessException] {
            if (-not [IO.File]::Exists($tempExe)) { throw }
            $versionName = 'HanLin-ToDo-{0:yyyyMMdd-HHmmss-fff}.exe' -f (Get-Date)
            $destination = Join-Path $outputPath $versionName
            [IO.File]::Move($tempExe, $destination)
            Write-Warning "The existing executable could not be replaced. Published $versionName."
        }
    }
    else {
        [IO.File]::Move($tempExe, $destination)
    }
}
finally {
    if ([IO.File]::Exists($tempExe)) {
        Remove-Item -LiteralPath $tempExe
    }
}

$installedHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
if ($installedHash -ne $packedHash) {
    throw 'The installed executable checksum does not match the build.'
}
if (-not $SkipLaunch) {
    Start-Process -FilePath $destination
}

[pscustomobject]@{
    Path = $destination
    Size = (Get-Item -LiteralPath $destination).Length
    SHA256 = $installedHash
    Launched = (-not $SkipLaunch)
}
