$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$appProject = Join-Path $root "src\NotebookAutoBrightness\NotebookAutoBrightness.csproj"
$installerProject = Join-Path $root "src\Installer\Installer.csproj"

$distDir = Join-Path $root "dist"
$appOut = Join-Path $distDir "app"
$installerOut = Join-Path $distDir "installer"
$payloadZip = Join-Path $root "src\Installer\payload.zip"

Write-Host "Publishing app..." -ForegroundColor Cyan
dotnet publish $appProject -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o $appOut

if (Test-Path $payloadZip) {
    Remove-Item $payloadZip -Force
}

Write-Host "Creating payload.zip..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $appOut "*") -DestinationPath $payloadZip -Force

Write-Host "Publishing installer..." -ForegroundColor Cyan
dotnet publish $installerProject -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true -o $installerOut

Write-Host "Done. Installer output:" -ForegroundColor Green
Write-Host (Join-Path $installerOut "NotebookAutoBrightnessSetup.exe")
