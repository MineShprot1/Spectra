param([string]$Action)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
function Installed {
 $rows = @()
 foreach ($p in @(Get-AppxPackage | Where-Object { $_.Name -match '^Microsoft\.Minecraft(UWP|WindowsBeta|Windows|WindowsPreview)$' })) {
  $manifest = Get-AppxPackageManifest -Package $p.PackageFullName
  foreach ($app in @($manifest.Package.Applications.Application)) {
   if (-not $app.Id) { continue }
   $rows += [pscustomobject]@{ id = "$($p.PackageFullName)!$($app.Id)"; name = $p.Name; version = $p.Version.ToString(); family = $p.PackageFamilyName; appId = [string]$app.Id; preview = ($p.Name -match 'Beta|Preview'); legacy = ($p.Version.Major -eq 0 -or ($p.Version.Major -eq 1 -and $p.Version.Minor -lt 2)); installed = $true }
  }
 }
 return $rows
}
try {
 switch ($Action) {
  'list' { $rows = @(Installed); ConvertTo-Json -InputObject $rows -Depth 5 -Compress }
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
  default { throw 'Unknown Bedrock operation' }
 }
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
