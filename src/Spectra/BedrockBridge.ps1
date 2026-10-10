param([string]$Action)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
function Installed {
 $rows = @()
 foreach ($p in @(Get-AppxPackage | Where-Object { $_.Name -match '^Microsoft\.Minecraft(UWP|WindowsBeta|Windows|WindowsPreview)$' })) {
  try { $manifest = Get-AppxPackageManifest -Package $p.PackageFullName } catch { continue }
  foreach ($app in @($manifest.Package.Applications.Application)) {
   if (-not $app.Id) { continue }
   $rows += [pscustomobject]@{ id = "$($p.PackageFullName)!$($app.Id)"; name = $p.Name; version = $p.Version.ToString(); family = $p.PackageFamilyName; appId = [string]$app.Id; preview = ($p.Name -match 'Beta|Preview'); legacy = ($p.Version.Major -eq 0 -or ($p.Version.Major -eq 1 -and $p.Version.Minor -lt 2)); installed = $true }
  }
 }
 # GetStartApps also exposes registered launch entries when package enumeration misses a GDK entry.
 foreach ($app in @(Get-StartApps -ErrorAction SilentlyContinue)) {
  if ($app.AppID -notmatch '^(Microsoft\.Minecraft(?:UWP|WindowsBeta|Windows|WindowsPreview)_8wekyb3d8bbwe)!([A-Za-z0-9_.-]+)$') { continue }
  $family = $Matches[1]; $appId = $Matches[2]; $name = $family -replace '_8wekyb3d8bbwe$',''
  if (@($rows | Where-Object { $_.family -eq $family -and $_.appId -eq $appId }).Count -gt 0) { continue }
  $rows += [pscustomobject]@{ id = "$family!$appId"; name = $name; version = '0.0.0.0'; family = $family; appId = $appId; preview = ($name -match 'Beta|Preview'); legacy = $false; installed = $true }
 }
 return $rows
}
try {
 switch ($Action) {
  'list' { $rows = @(Installed); ConvertTo-Json -InputObject $rows -Depth 5 -Compress }
  'officialLauncher' {
   $launcher = Get-StartApps | Where-Object { $_.AppID -match '^(Microsoft\.4297127D64EC6|Microsoft\.MinecraftLauncher)_8wekyb3d8bbwe![A-Za-z0-9_.-]+$' } | Select-Object -First 1
   if ($launcher) { Start-Process -FilePath 'explorer.exe' -ArgumentList ("shell:AppsFolder\" + $launcher.AppID) | Out-Null }
   else {
    $candidates = @((Join-Path ${env:ProgramFiles} 'Minecraft Launcher\MinecraftLauncher.exe'))
    if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} 'Minecraft Launcher\MinecraftLauncher.exe' }
    $exe = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $exe) { throw 'Official Minecraft Launcher not found. Open it manually to update Minecraft for Windows.' }
    Start-Process -FilePath $exe | Out-Null
   }
   ConvertTo-Json -InputObject @() -Compress
  }
  'install' {
   if (Get-Process -Name 'Minecraft.Windows' -ErrorAction SilentlyContinue) { throw 'Close Minecraft before installation.' }
   $name = $env:SPECTRA_BEDROCK_NAME
   $version = $env:SPECTRA_BEDROCK_VERSION
   if ($name -notmatch '^Microsoft\.Minecraft(UWP|WindowsBeta|Windows|WindowsPreview)$' -or $version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Missing expected package identity.' }
   if ([IO.Path]::GetExtension($env:SPECTRA_BEDROCK_PACKAGE) -notin @('.appx','.msix')) { throw 'MSIXVC requires Xbox Gaming Services installation, not Add-AppxPackage.' }
   if (@(Get-AppxPackage -Name $name | Where-Object { $_.Version.ToString() -ne $version }).Count -gt 0) { throw 'Installation would replace the existing Minecraft version; cancelled.' }
   Add-AppxPackage -Path $env:SPECTRA_BEDROCK_PACKAGE -ErrorAction Stop | Out-Null
   ConvertTo-Json -InputObject @(Installed) -Depth 5 -Compress
  }
  'replace' {
   if (Get-Process -Name 'Minecraft.Windows' -ErrorAction SilentlyContinue) { throw 'Close Minecraft before installation.' }
   $name = $env:SPECTRA_BEDROCK_NAME
   $version = $env:SPECTRA_BEDROCK_VERSION
   if ($name -notmatch '^Microsoft\.Minecraft(UWP|WindowsBeta|Windows|WindowsPreview)$' -or $version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Missing expected package identity.' }
   if ([IO.Path]::GetExtension($env:SPECTRA_BEDROCK_PACKAGE) -notin @('.appx','.msix')) { throw 'MSIXVC requires Xbox Gaming Services installation, not Add-AppxPackage.' }
   # Same-identity switch: user data (worlds, settings) is preserved; Spectra already made a worlds backup.
   foreach ($old in @(Get-AppxPackage -Name $name | Where-Object { $_.Version.ToString() -ne $version })) {
    Remove-AppxPackage -Package $old.PackageFullName -PreserveApplicationData -ErrorAction Stop
   }
   Add-AppxPackage -Path $env:SPECTRA_BEDROCK_PACKAGE -ErrorAction Stop | Out-Null
   ConvertTo-Json -InputObject @(Installed) -Depth 5 -Compress
  }
  default { throw 'Unknown Bedrock operation' }
 }
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
