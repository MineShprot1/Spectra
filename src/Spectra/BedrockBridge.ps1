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
    if (-not $exe) { Start-Process 'https://www.minecraft.net/download'; ConvertTo-Json -InputObject @() -Compress; exit 0 }
    Start-Process -FilePath $exe | Out-Null
   }
   # Best-effort navigation to the Minecraft for Windows tab using Windows UI Automation.
   # The Launcher does not publish a stable Bedrock-page command-line/deep-link contract.
   try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    for ($attempt = 0; $attempt -lt 12; $attempt++) {
     $process = Get-Process -Name 'MinecraftLauncher' -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
     if ($process) {
      $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
      $elements = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
      $tab = $elements | Where-Object { $_.Current.Name -match '^(Minecraft (for|для|für|para|pour|per) Windows|Minecraft: Windows( Edition)?)$' } | Select-Object -First 1
      if ($tab) {
       $pattern = $null
       if ($tab.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$pattern)) { $pattern.Invoke(); break }
       if ($tab.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern,[ref]$pattern)) { $pattern.Select(); break }
      }
     }
     Start-Sleep -Milliseconds 500
    }
   } catch { # The launcher still opens; select Minecraft for Windows manually if UI Automation is unavailable.
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
  'storeUpdate' {
   if (Get-Process -Name 'Minecraft.Windows' -ErrorAction SilentlyContinue) { throw 'Close Minecraft before update.' }
   $target = @(Installed) | Where-Object { -not $_.preview } | Select-Object -First 1
   if (-not $target) { throw 'Minecraft is not installed.' }
   Add-Type -AssemblyName System.Runtime.WindowsRuntime
   [void][Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallManager, Windows.ApplicationModel.Store.Preview, ContentType = WindowsRuntime]
   $asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
   $manager = New-Object Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallManager
   $itemType = [Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallItem]
   try {
    $task = $asTask.MakeGenericMethod($itemType).Invoke($null, @($manager.UpdateAppByPackageFamilyNameAsync($target.family)))
    $task.Wait(120000) | Out-Null
    $item = $task.Result
   } catch {
    $inner = $_.Exception
    while ($inner.InnerException) { $inner = $inner.InnerException }
    throw ('Store API: {0} (0x{1:X8}): {2}' -f $inner.GetType().Name, $inner.HResult, $inner.Message)
   }
   if ($item) {
    $deadline = (Get-Date).AddMinutes(25)
    while ((Get-Date) -lt $deadline) {
     $state = [string]$item.GetCurrentStatus().InstallState
     if ($state -eq 'Completed') { break }
     if ($state -in @('Error', 'Canceled', 'Paused', 'PausedLowBattery', 'PausedWiFiRecommended', 'PausedWiFiRequired')) { throw "Store update stopped: $state" }
     Start-Sleep -Seconds 2
    }
    if ((Get-Date) -ge $deadline) { throw 'Store update timed out.' }
   }
   ConvertTo-Json -InputObject @(Installed) -Depth 5 -Compress
  }
  'register' {
   if (Get-Process -Name 'Minecraft.Windows' -ErrorAction SilentlyContinue) { throw 'Close Minecraft before registration.' }
   $name = $env:SPECTRA_BEDROCK_NAME
   $version = $env:SPECTRA_BEDROCK_VERSION
   $manifest = $env:SPECTRA_BEDROCK_PACKAGE
   if ($name -notmatch '^Microsoft\.Minecraft(UWP|WindowsBeta|Windows|WindowsPreview)$' -or $version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Missing expected package identity.' }
   if ((Split-Path -Leaf $manifest) -ne 'AppxManifest.xml' -or -not (Test-Path -LiteralPath $manifest -PathType Leaf)) { throw 'Bad package manifest.' }
   # Check developer mode BEFORE touching the installed game, so a failed registration cannot leave the PC without Minecraft.
   $dev = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
   if ($dev -ne 1) { throw 'Enable Windows Developer Mode (Settings - Privacy and security - For developers), then try again.' }
   foreach ($old in @(Get-AppxPackage -Name $name)) {
    try { Remove-AppxPackage -Package $old.PackageFullName -PreserveApplicationData -ErrorAction Stop }
    catch {
     if ($_.Exception.Message -match '0x80073CFA|PreserveApplicationData') { Remove-AppxPackage -Package $old.PackageFullName -ErrorAction Stop } else { throw }
    }
   }
   Add-AppxPackage -Register $manifest -ErrorAction Stop | Out-Null
   ConvertTo-Json -InputObject @(Installed) -Depth 5 -Compress
  }
  'removeVersion' {
   if (Get-Process -Name 'Minecraft.Windows' -ErrorAction SilentlyContinue) { throw 'Close Minecraft before deleting a version.' }
   $name = $env:SPECTRA_BEDROCK_NAME
   if ($name -notmatch '^Microsoft\.Minecraft(UWP|WindowsBeta|Windows|WindowsPreview)$') { throw 'Invalid Minecraft identity.' }
   $selected = Get-AppxPackage -Name $name | Where-Object { ($_.PackageFullName -eq ($env:SPECTRA_BEDROCK_PACKAGE -split '!')[0] -or $_.PackageFamilyName -eq ($env:SPECTRA_BEDROCK_PACKAGE -split '!')[0]) -and ($env:SPECTRA_BEDROCK_VERSION -eq '0.0.0.0' -or $_.Version.ToString() -eq $env:SPECTRA_BEDROCK_VERSION) } | Select-Object -First 1
   if (-not $selected) { throw 'Installed Minecraft package not found.' }
   Remove-AppxPackage -Package $selected.PackageFullName -ErrorAction Stop | Out-Null
   ConvertTo-Json -InputObject @(Installed) -Depth 5 -Compress
  }
  'unregister' {
   if (Get-Process -Name 'Minecraft.Windows' -ErrorAction SilentlyContinue) { throw 'Close Minecraft before unregistering.' }
   foreach ($old in @(Get-AppxPackage | Where-Object { $_.Name -match '^Microsoft\.Minecraft(UWP|WindowsBeta|Windows|WindowsPreview)$' -and $_.IsDevelopmentMode })) {
    Remove-AppxPackage -Package $old.PackageFullName -PreserveApplicationData -ErrorAction Stop
   }
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
    try { Remove-AppxPackage -Package $old.PackageFullName -PreserveApplicationData -ErrorAction Stop }
    catch {
     # Store-installed packages reject PreserveApplicationData (0x80073CFA); Spectra has already copied com.mojang and restores it afterwards.
     if ($_.Exception.Message -match '0x80073CFA|PreserveApplicationData') { Remove-AppxPackage -Package $old.PackageFullName -ErrorAction Stop } else { throw }
    }
   }
   Add-AppxPackage -Path $env:SPECTRA_BEDROCK_PACKAGE -ErrorAction Stop | Out-Null
   ConvertTo-Json -InputObject @(Installed) -Depth 5 -Compress
  }
  default { throw 'Unknown Bedrock operation' }
 }
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
