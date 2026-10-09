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
  'install' { if (Get-Process -Name 'Minecraft.Windows' -ErrorAction SilentlyContinue) { throw 'Close Minecraft before switching its version.' }; Add-AppxPackage -Path $env:SPECTRA_BEDROCK_PACKAGE -ForceUpdateFromAnyVersion -ErrorAction Stop | Out-Null; ConvertTo-Json -InputObject @(Installed) -Depth 5 -Compress }
  default { throw 'Unknown Bedrock operation' }
 }
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
