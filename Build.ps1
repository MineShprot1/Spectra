$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install .NET SDK 8 from https://dotnet.microsoft.com/download/dotnet/8.0' }
dotnet restore src/Spectra/Spectra.csproj
if ($LASTEXITCODE) { throw 'Restore failed' }
dotnet build src/Spectra/Spectra.csproj -c Release --no-restore
if ($LASTEXITCODE) { throw 'Build failed' }
dotnet run --project tests/Spectra.Tests.csproj -c Release
if ($LASTEXITCODE) { throw 'Tests failed' }
dotnet publish src/Spectra/Spectra.csproj -c Release -r win-x64 --self-contained true -o dist/Spectra
if ($LASTEXITCODE) { throw 'Publish failed' }
Write-Host "Ready: $PSScriptRoot\dist\Spectra\Spectra.exe"
