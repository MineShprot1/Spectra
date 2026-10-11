param([switch]$SkipLauncherBuild)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
# Build and verify the launcher using the existing build script.
if (-not $SkipLauncherBuild) { & "$PSScriptRoot/Build.ps1" }
if (-not (Test-Path "$PSScriptRoot/dist/Spectra/Spectra.exe")) { throw "Publish the launcher before building its installer" }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$staging = Join-Path ([IO.Path]::GetTempPath()) ('SpectraPayload-' + [Guid]::NewGuid().ToString('N'))
$payload = "$PSScriptRoot/src/Spectra.Installer/payload.zip"
try {
 New-Item -ItemType Directory -Path $staging | Out-Null
 Copy-Item -Path "$PSScriptRoot/dist/Spectra/*" -Destination $staging -Recurse
 # Never distribute personal API keys, accounts or local data in an installer.
 Get-ChildItem -LiteralPath $staging -Recurse -File | Where-Object { $_.Name -in @('api-keys.local.json','connections.dat','session.dat','microsoft-oauth.json','install-preferences.json') } | Remove-Item -Force
 if (Test-Path -LiteralPath $payload) { Remove-Item -LiteralPath $payload -Force }
 [IO.Compression.ZipFile]::CreateFromDirectory($staging,$payload,[IO.Compression.CompressionLevel]::Optimal,$false)
 dotnet publish src/Spectra.Installer/Spectra.Installer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/Installer
 if ($LASTEXITCODE) { throw 'Installer publish failed' }
 [xml]$project = Get-Content "$PSScriptRoot/src/Spectra.Installer/Spectra.Installer.csproj"
 $version = [string]$project.Project.PropertyGroup.Version
 $archive = "$PSScriptRoot/dist/Spectra-$version-setup.zip"
 Compress-Archive -LiteralPath "$PSScriptRoot/dist/Installer/SpectraSetup.exe" -DestinationPath $archive -Force
 Write-Host "Archive: $archive"
 Write-Host "Ready: $PSScriptRoot\dist\Installer\SpectraSetup.exe"
} finally { if(Test-Path -LiteralPath $staging){Remove-Item -LiteralPath $staging -Recurse -Force} }
