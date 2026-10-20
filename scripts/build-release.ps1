# Release build -> dist/ASH-Setup-<Version>.exe (single exe, plugin DLLs embedded) and dist/ASH-Plugin-<Version>.zip (manual NETLOAD).
# Version comes from <Version> in Directory.Build.props.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath("$PSScriptRoot\..")
$version = ([xml](Get-Content "$root\Directory.Build.props" -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Directory.Build.props has no <Version>" }
dotnet build "$root\ASH.sln" -c Release -nologo -v q
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$dist = "$root\dist"
New-Item -ItemType Directory -Force $dist | Out-Null
$exe = "$dist\ASH-Setup-$version.exe"
Copy-Item "$root\src\ASH.Installer\bin\Release\net48\ASH.Installer.exe" $exe -Force
$plugin = "$root\src\ASH.Plugin\bin\Release\net48"
$zip = "$dist\ASH-Plugin-$version.zip"
Compress-Archive -Path "$plugin\ASH.Plugin.dll", "$plugin\ASH.Core.dll" -DestinationPath $zip -Force
Get-Item $exe, $zip | ForEach-Object { Write-Host ("{0}  {1:N0} bytes" -f $_.FullName, $_.Length) }
