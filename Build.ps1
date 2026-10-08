$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install .NET SDK 8 from https://dotnet.microsoft.com/download/dotnet/8.0' }
$gitCommand = Get-Command git -CommandType Application -ErrorAction SilentlyContinue
if ($gitCommand) {
 # Git metadata is optional: source ZIPs do not contain a .git directory.
 # Redirect through Process so Windows PowerShell 5.1 does not turn Git's
 # stderr into a terminating NativeCommandError under ErrorActionPreference.
 $commitProcess = $null
 try {
  $gitInfo = New-Object System.Diagnostics.ProcessStartInfo
  $gitInfo.FileName = $gitCommand.Source
  $gitInfo.Arguments = 'rev-parse HEAD'
  $gitInfo.WorkingDirectory = $PSScriptRoot
  $gitInfo.UseShellExecute = $false
  $gitInfo.CreateNoWindow = $true
  $gitInfo.RedirectStandardOutput = $true
  $gitInfo.RedirectStandardError = $true
  $commitProcess = New-Object System.Diagnostics.Process
  $commitProcess.StartInfo = $gitInfo
  [void]$commitProcess.Start()
  $sourceCommit = $commitProcess.StandardOutput.ReadToEnd().Trim()
  [void]$commitProcess.StandardError.ReadToEnd()
  $commitProcess.WaitForExit()
  if ($commitProcess.ExitCode -eq 0 -and $sourceCommit -match '^[0-9a-f]{40}$') {
   [IO.File]::WriteAllText("$PSScriptRoot/src/Spectra/build-commit.txt", $sourceCommit)
  }
 } catch {
  Write-Warning 'Could not read optional Git metadata; continuing the build.'
 } finally {
  if ($null -ne $commitProcess) { $commitProcess.Dispose() }
 }
}
dotnet restore src/Spectra/Spectra.csproj
if ($LASTEXITCODE) { throw 'Restore failed' }
dotnet build src/Spectra/Spectra.csproj -c Release --no-restore
if ($LASTEXITCODE) { throw 'Build failed' }
dotnet run --project tests/Spectra.Tests.csproj -c Release
if ($LASTEXITCODE) { throw 'Tests failed' }
dotnet publish src/Spectra/Spectra.csproj -c Release -r win-x64 --self-contained true -o dist/Spectra
if ($LASTEXITCODE) { throw 'Publish failed' }
if (Test-Path "$PSScriptRoot/api-keys.local.json") { Copy-Item "$PSScriptRoot/api-keys.local.json" "$PSScriptRoot/dist/Spectra/api-keys.local.json" }
Write-Host "Ready: $PSScriptRoot\dist\Spectra\Spectra.exe"