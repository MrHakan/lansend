param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [ValidateSet("win-x64", "win-arm64")]
    [string] $Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$projectPath = Join-Path $PSScriptRoot "LANSEND.csproj"
$outputPath = Join-Path $PSScriptRoot "publish"

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $outputPath

Copy-Item -Path (Join-Path $PSScriptRoot "Scripts") -Destination $outputPath -Recurse -Force
Write-Host "Yayın hazır: $outputPath"
Write-Host "Kurulum: Set-ExecutionPolicy -Scope Process Bypass; .\Scripts\Install-LANSEND.ps1 -AppPath .\LANSEND.exe"
