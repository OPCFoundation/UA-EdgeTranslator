# Publishes every protocol driver as Debug into UAServer\drivers\<name> using its
# FolderProfile.pubxml, then verifies the deployed DLL matches the bin\Debug output.
# Configuration (and for Matter the TargetFramework) are passed explicitly because
# dotnet publish does not honor them from the publish profile.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$logDir = Join-Path $root 'publish-logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

$failed = @()
$profiles = Get-ChildItem -Path (Join-Path $root 'ProtocolDrivers') -Recurse -Filter 'FolderProfile.pubxml'

foreach ($pubxml in $profiles) {
	$projectDir = $pubxml.Directory.Parent.Parent.FullName
	$csproj = Get-ChildItem -Path $projectDir -Filter '*.csproj' | Select-Object -First 1
	$name = $csproj.BaseName
	$log = Join-Path $logDir "$name.log"

	$tfArgs = @()
	if ($name -eq 'Matter') { $tfArgs = @('-f', 'net10.0') }

	& dotnet publish $csproj.FullName -c Debug -p:PublishProfile=FolderProfile @tfArgs *> $log
	if ($LASTEXITCODE -ne 0) {
		Write-Host "FAIL  $name (publish failed, see $log)"
		$failed += $name
		continue
	}

	# Resolve the built assembly, the TFM and the publish folder from MSBuild itself.
	$msbuildArgs = @($csproj.FullName, '-p:Configuration=Debug', '-p:PublishProfile=FolderProfile')
	if ($name -eq 'Matter') { $msbuildArgs += '-p:TargetFramework=net10.0' }
	$props = & dotnet msbuild @msbuildArgs -getProperty:TargetFileName -getProperty:TargetFramework -getProperty:PublishDir | ConvertFrom-Json
	$p = $props.Properties
	$tfm = if ($name -eq 'Matter') { 'net10.0' } else { $p.TargetFramework }
	$built = Join-Path $projectDir "bin\Debug\$tfm\$($p.TargetFileName)"
	$publishDir = if ([System.IO.Path]::IsPathRooted($p.PublishDir)) { $p.PublishDir } else { Join-Path $projectDir $p.PublishDir }
	$deployed = Join-Path $publishDir $p.TargetFileName

	if (-not (Test-Path $built) -or -not (Test-Path $deployed)) {
		Write-Host "FAIL  $name (missing $built or $deployed)"
		$failed += $name
		continue
	}

	if ((Get-FileHash $built).Hash -ne (Get-FileHash $deployed).Hash) {
		Write-Host "FAIL  $name (deployed DLL differs from bin\Debug)"
		$failed += $name
		continue
	}

	Write-Host "OK    $name -> $deployed"
}

if ($failed.Count -gt 0) {
	Write-Host "`n$($failed.Count) driver(s) failed: $($failed -join ', ')"
	exit 1
}

Write-Host "`nAll $($profiles.Count) drivers published and verified."
