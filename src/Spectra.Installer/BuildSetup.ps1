param([Parameter(Mandatory=$true)][string]$LauncherFolder)
$ErrorActionPreference = 'Stop'
$launcher = (Resolve-Path -LiteralPath $LauncherFolder).Path
if (-not (Test-Path -LiteralPath (Join-Path $launcher 'Spectra.exe'))) { throw 'LauncherFolder must contain the published Spectra.exe and its dependencies.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$staging = Join-Path ([IO.Path]::GetTempPath()) ('SpectraPayload-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $PSScriptRoot 'payload.zip'
$output = Join-Path $PSScriptRoot 'dist'
try {
 New-Item -ItemType Directory -Path $staging | Out-Null
 Copy-Item -Path (Join-Path $launcher '*') -Destination $staging -Recurse
 Get-ChildItem -LiteralPath $staging -Recurse -File | Where-Object { $_.Name -in @('api-keys.local.json','connections.dat','session.dat','microsoft-oauth.json','install-preferences.json') } | Remove-Item -Force
 if (Test-Path -LiteralPath $payload) { Remove-Item -LiteralPath $payload -Force }
 [IO.Compression.ZipFile]::CreateFromDirectory($staging,$payload,[IO.Compression.CompressionLevel]::Optimal,$false)
 dotnet publish (Join-Path $PSScriptRoot 'Spectra.Installer.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $output
 if ($LASTEXITCODE) { throw 'Installer publish failed' }
 [xml]$project = Get-Content (Join-Path $PSScriptRoot 'Spectra.Installer.csproj')
 $version = [string]$project.Project.PropertyGroup.Version
 $archive = Join-Path $output "Spectra-$version-setup.zip"
 Compress-Archive -LiteralPath (Join-Path $output 'SpectraSetup.exe') -DestinationPath $archive -Force
 Write-Host "Ready: $archive"
} finally {
 if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
