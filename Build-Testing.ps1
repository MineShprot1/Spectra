param([ValidateRange(1,65534)][int]$Number)
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
if(-not $PSBoundParameters.ContainsKey('Number')){$Number=(Get-Content testing-build.json -Raw | ConvertFrom-Json).number}
if($Number -lt 1 -or $Number -gt 65534){throw 'Invalid testing build number'}
try{
 $commit=& git rev-parse HEAD 2>$null
 if($LASTEXITCODE -eq 0 -and $commit -match '^[0-9a-f]{40}$'){
  [IO.File]::WriteAllText("$PSScriptRoot/src/Spectra/build-commit.txt",$commit.Trim())
  $subject=& git log -1 --format=%s
  [IO.File]::WriteAllText("$PSScriptRoot/src/Spectra/build-commit-name.txt",$subject.Trim())
 }
}catch{Write-Warning 'Git metadata unavailable; using assembly metadata.'}
# Independent assembly/intermediate/output folders prevent a test publish from
# contaminating the normal distribution. MSBuild owns the test version scheme.
dotnet publish src/Spectra/Spectra.csproj -c Release -r win-x64 --self-contained true -p:SpectraChannel=testing "-p:TestingBuildNumber=$Number" -o dist/Spectra-Testing
if($LASTEXITCODE){throw 'Testing publish failed'}
Write-Host "Ready: $PSScriptRoot\dist\Spectra-Testing\Spectra.exe (0.1.0-test.$Number)"
