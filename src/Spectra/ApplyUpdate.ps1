param([Parameter(Mandatory=$true)][string]$JobFile)
# Enable long paths in Windows PowerShell's .NET Framework host.
[AppContext]::SetSwitch('Switch.System.IO.UseLegacyPathHandling', $false)
[AppContext]::SetSwitch('Switch.System.IO.BlockLongPaths', $false)
$ErrorActionPreference='Stop'
$task=Get-Content -LiteralPath $JobFile -Raw | ConvertFrom-Json
$backup=Join-Path $task.job 'backup'
$changed=New-Object 'System.Collections.Generic.List[string]'
$created=New-Object 'System.Collections.Generic.List[string]'
function Under([string]$Root,[string]$Relative) {
 $prefix=[IO.Path]::GetFullPath($Root).TrimEnd('\')+'\'
 $path=[IO.Path]::GetFullPath((Join-Path $Root $Relative))
 if(-not $path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid update path'}
 return $path
}
try {
 $running=Get-Process -Id $task.pid -ErrorAction SilentlyContinue
 if($running){Wait-Process -Id $task.pid -Timeout 45 -ErrorAction Stop}
 New-Item -ItemType Directory -Path $backup -Force | Out-Null
 $fresh=@(Get-Content -LiteralPath (Join-Path $task.publish 'installed-files.json') -Raw | ConvertFrom-Json)+@('installed-files.json')
 $oldManifest=Join-Path $task.install 'installed-files.json'
 $old=@();if(Test-Path -LiteralPath $oldManifest){$old=@(Get-Content -LiteralPath $oldManifest -Raw | ConvertFrom-Json)+@('installed-files.json')}
 foreach($relative in @($fresh+$old | Select-Object -Unique)) {
  $destination=Under $task.install $relative
  $incoming=Under $task.publish $relative
  $hasFresh=$fresh -contains $relative
  if($hasFresh -and (Test-Path -LiteralPath $destination) -and ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $incoming -Algorithm SHA256).Hash)){continue}
  if(Test-Path -LiteralPath $destination){$saved=Under $backup $relative;New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($saved)) -Force | Out-Null;Copy-Item -LiteralPath $destination -Destination $saved;$changed.Add($relative)}else{$created.Add($relative)}
  if($hasFresh){New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null;Copy-Item -LiteralPath $incoming -Destination $destination -Force}else{Remove-Item -LiteralPath $destination -Force}
 }
 # Source cache is disposable; every cached blob is verified before reuse.
 try {if(Test-Path -LiteralPath $task.cache){Remove-Item -LiteralPath $task.cache -Recurse -Force};Move-Item -LiteralPath $task.source -Destination $task.cache} catch {Write-Warning 'Source cache could not be moved; next update will download it again.'}
 if(Test-Path -LiteralPath $task.failureMarker){Remove-Item -LiteralPath $task.failureMarker -Force}
 Start-Process -FilePath (Join-Path $task.install 'Spectra.exe') -WorkingDirectory $task.install
} catch {
 $failure=$_.Exception.Message
 $task.commit | Set-Content -LiteralPath $task.failureMarker
 foreach($relative in $changed){try{Copy-Item -LiteralPath (Under $backup $relative) -Destination (Under $task.install $relative) -Force}catch{Write-Warning "Restore failed: $relative"}}
 foreach($relative in $created){try{Remove-Item -LiteralPath (Under $task.install $relative) -Force -ErrorAction SilentlyContinue}catch{}}
 $failure | Set-Content -LiteralPath (Join-Path $task.job 'apply-error.txt')
 Write-Host "Update failed. Previous files restored where possible. $failure"
 if(-not (Get-Process -Id $task.pid -ErrorAction SilentlyContinue)){Start-Process -FilePath (Join-Path $task.install 'Spectra.exe') -WorkingDirectory $task.install -ErrorAction SilentlyContinue}
 Read-Host 'Press Enter to close'
 exit 1
}
# Keep backup in the update job for manual recovery; no game/config folders are copied.
