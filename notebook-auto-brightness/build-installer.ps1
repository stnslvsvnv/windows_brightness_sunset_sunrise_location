$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$appProject = Join-Path $root "src\NotebookAutoBrightness\NotebookAutoBrightness.csproj"
$installerProject = Join-Path $root "src\Installer\Installer.csproj"

$distDir = Join-Path $root "dist"
$appOut = Join-Path $distDir "app"
$installerOut = Join-Path $distDir "installer"
$payloadZip = Join-Path $root "src\Installer\payload.zip"
$repoRootInstaller = Join-Path (Split-Path -Parent $root) "NotebookAutoBrightnessSetup.exe"

function Invoke-Dotnet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

Write-Host "Publishing app..." -ForegroundColor Cyan
Invoke-Dotnet @(
    "restore", $appProject,
    "-r", "win-x64"
)
Invoke-Dotnet @(
    "publish", $appProject,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "--no-restore",
    "/p:PublishSingleFile=true",
    "/p:IncludeNativeLibrariesForSelfExtract=true",
    "/p:EnableCompressionInSingleFile=true",
    "-o", $appOut
)

if (Test-Path $payloadZip) {
    Remove-Item $payloadZip -Force
}

Write-Host "Creating payload.zip..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $appOut "*") -DestinationPath $payloadZip -Force

Write-Host "Publishing installer..." -ForegroundColor Cyan
Invoke-Dotnet @(
    "restore", $installerProject,
    "-r", "win-x64"
)
Invoke-Dotnet @(
    "publish", $installerProject,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "--no-restore",
    "/p:PublishSingleFile=true",
    "/p:IncludeNativeLibrariesForSelfExtract=true",
    "/p:EnableCompressionInSingleFile=true",
    "-o", $installerOut
)

Write-Host "Copying installer to repository root..." -ForegroundColor Cyan
Copy-Item (Join-Path $installerOut "NotebookAutoBrightnessSetup.exe") $repoRootInstaller -Force

Write-Host "Done. Installer output:" -ForegroundColor Green
Write-Host (Join-Path $installerOut "NotebookAutoBrightnessSetup.exe")
