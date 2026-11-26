#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\installer\output'),
    [string]$StagingDirectory = (Join-Path $PSScriptRoot '..\obj\installer'),
    [string]$DotNet = 'dotnet',
    [string]$Iscc
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$stagingPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($StagingDirectory)
$project = Join-Path $repo 'src\HanLin.ToDo\HanLin.ToDo.csproj'
$installer = Join-Path $repo 'installer\HanLin-ToDo.iss'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version

if (-not $Iscc) {
    $Iscc = (Get-Command ISCC.exe -CommandType Application -ErrorAction SilentlyContinue).Source
}
if (-not $Iscc) {
    $Iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
              "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
              "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $Iscc) { throw '找不到 ISCC.exe，请使用 Inno Setup 6.6 或更新版本。' }

& (Join-Path $PSScriptRoot 'Publish-Windows.ps1') -DotNet $DotNet -StagingDirectory $stagingPath -BuildOnly
[void][IO.Directory]::CreateDirectory($outputPath)
& $Iscc "/DPayloadDir=$stagingPath" "/DMyAppVersion=$version" "/O$outputPath" $installer
if ($LASTEXITCODE -ne 0) { throw "安装包编译失败，退出码 $LASTEXITCODE。" }

$package = Get-Item -LiteralPath (Join-Path $outputPath "HanLin-ToDo-Setup-$version.exe")
if ($package.Length -le 0) { throw '安装包为空。' }
$package.FullName
