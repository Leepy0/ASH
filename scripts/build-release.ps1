# Release build -> dist/ASH-Setup-<Version>.exe (single exe, plugin DLLs embedded), dist/ASH-Plugin-<Version>.zip (manual NETLOAD)
# and dist/ASH-Runner-<Version>.zip (ash-run, framework-dependent net10.0: needs the .NET 10 runtime).
# Version comes from <Version> in Directory.Build.props.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath("$PSScriptRoot\..")
$version = ([xml](Get-Content "$root\Directory.Build.props" -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Directory.Build.props has no <Version>" }
dotnet build "$root\ASH.sln" -c Release -nologo -v q
if ($LASTEXITCODE) { throw "dotnet build failed (exit $LASTEXITCODE)" }
dotnet test "$root\tests\ASH.Tests" -c Release -nologo -v q
if ($LASTEXITCODE) { throw "dotnet test failed (exit $LASTEXITCODE); release not packaged" }
$dist = "$root\dist"
New-Item -ItemType Directory -Force $dist | Out-Null
$exe = "$dist\ASH-Setup-$version.exe"
Copy-Item "$root\src\ASH.Installer\bin\Release\net48\ASH.Installer.exe" $exe -Force
$plugin = "$root\src\ASH.Plugin\bin\Release\net48"
$zip = "$dist\ASH-Plugin-$version.zip"
Compress-Archive -Path "$plugin\ASH.Plugin.dll", "$plugin\ASH.Core.dll" -DestinationPath $zip -Force
$pub = "$root\src\ASH.Runner\bin\Release\publish"
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
dotnet publish "$root\src\ASH.Runner" -c Release -o $pub --self-contained false -nologo -v q
if ($LASTEXITCODE) { throw "dotnet publish ash-run failed (exit $LASTEXITCODE)" }
$runnerZip = "$dist\ASH-Runner-$version.zip"
Compress-Archive -Path "$pub\*" -DestinationPath $runnerZip -Force
Get-Item $exe, $zip, $runnerZip | ForEach-Object { Write-Host ("{0}  {1:N0} bytes" -f $_.FullName, $_.Length) }
