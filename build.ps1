$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir = Join-Path $projectDir 'src'
$distDir = Join-Path $projectDir 'dist'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'C# compiler was not found (.NET Framework 4.x).'
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$legacyExe = Join-Path $distDir 'StalcraftResolutionSwitcher.exe'
if (Test-Path -LiteralPath $legacyExe) {
    Remove-Item -LiteralPath $legacyExe -Force
}

& $compiler `
    /nologo `
    /codepage:65001 `
    /target:winexe `
    /platform:anycpu `
    /optimize+ `
    /win32manifest:"$sourceDir\app.manifest" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /out:"$distDir\StalcraftResolutionMonitor.exe" `
    "$sourceDir\Common.cs" `
    "$sourceDir\MonitorProgram.cs"

if ($LASTEXITCODE -ne 0) {
    throw "Monitor build failed with exit code $LASTEXITCODE."
}

& $compiler `
    /nologo `
    /codepage:65001 `
    /target:exe `
    /platform:anycpu `
    /optimize+ `
    /win32manifest:"$sourceDir\app.manifest" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /out:"$distDir\StalcraftResolutionSettings.exe" `
    "$sourceDir\Common.cs" `
    "$sourceDir\SettingsProgram.cs"

if ($LASTEXITCODE -ne 0) {
    throw "Settings build failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $projectDir 'README.md') -Destination $distDir -Force
Copy-Item -LiteralPath (Join-Path $projectDir 'LICENSE') -Destination $distDir -Force

$packagePath = Join-Path $distDir 'StalcraftResolutionSwitcher-win.zip'
Compress-Archive `
    -LiteralPath "$distDir\StalcraftResolutionMonitor.exe", "$distDir\StalcraftResolutionSettings.exe", "$distDir\README.md", "$distDir\LICENSE" `
    -DestinationPath $packagePath `
    -CompressionLevel Optimal `
    -Force

Write-Host "Build completed:"
Write-Host "  $distDir\StalcraftResolutionMonitor.exe"
Write-Host "  $distDir\StalcraftResolutionSettings.exe"
Write-Host "  $packagePath"
