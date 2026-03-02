# Builds and publishes V2XBridge.exe as a self-contained single file
# to the repo root, ready to be committed and shipped with the SignalRGB addon.
# Requires: .NET 8 SDK  https://aka.ms/dotnet/download

$ErrorActionPreference = "Stop"

Write-Host "Building V2XBridge (self-contained, win-x64)..." -ForegroundColor Cyan

dotnet publish bridge/V2XBridge/V2XBridge.csproj /p:PublishProfile=Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Done. V2XBridge.exe is at the repo root." -ForegroundColor Green
Write-Host "Commit it alongside the plugin files and push." -ForegroundColor Green
