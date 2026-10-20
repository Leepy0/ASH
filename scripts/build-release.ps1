# Release build -> dist/ASH-Setup-<Version>.exe (single exe, plugin DLLs embedded), dist/ASH-Plugin-<Version>.zip (manual NETLOAD)
# and dist/ASH-Runner-<Version>-win-x64.zip (ash-run.exe, self-contained single file: no .NET install needed).
# Version comes from <Version> in Directory.Build.props.
# Also dist/ASH.json (release manifest: version, ASH_MIN_SUPPORTED or the current version as minSupported, SHA-256/size of the Setup exe)
# and dist/ASH.json.sig when $env:ASH_SIGNING_KEY points at the private-key PEM (AshRelease keygen). -RequireSignature fails without that key or without update-key.pub in the repo root;
# a signed manifest is verified against update-key.pub. -Notes <file> becomes the manifest notes.
param([switch]$RequireSignature, [string]$Notes)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath("$PSScriptRoot\..")
$version = ([xml](Get-Content "$root\Directory.Build.props" -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Directory.Build.props has no <Version>" }
$signKey = $env:ASH_SIGNING_KEY
$pubKey = "$root\update-key.pub"
if ($RequireSignature -and (-not $signKey -or -not (Test-Path $signKey))) { throw "-RequireSignature: set `$env:ASH_SIGNING_KEY to the signing key PEM path" }
if ($RequireSignature -and -not (Test-Path $pubKey)) { throw "-RequireSignature: $pubKey is missing (AshRelease keygen, then copy update-key.pub to the repo root)" }
if ($Notes -and -not (Test-Path $Notes)) { throw "-Notes file not found: $Notes" }
dotnet build "$root\ASH.sln" -c Release -nologo -v q
if ($LASTEXITCODE) { throw "dotnet build failed (exit $LASTEXITCODE)" }
dotnet test "$root\tests\ASH.Tests" -c Release -nologo -v q
if ($LASTEXITCODE) { throw "dotnet test failed (exit $LASTEXITCODE); release not packaged" }
$dist = "$root\dist"
New-Item -ItemType Directory -Force $dist | Out-Null
$exe = "$dist\ASH-Setup-$version.exe"
Copy-Item "$root\src\ASH.Installer\bin\Release\net48\ASH.Installer.exe" $exe -Force
$minSupported = if ($env:ASH_MIN_SUPPORTED) { $env:ASH_MIN_SUPPORTED } else { $version }
$ashRelease = "$root\tools\AshRelease\bin\Release\net10.0\AshRelease.dll"
$manifest = "$dist\ASH.json"
Remove-Item $manifest, "$manifest.sig" -Force -ErrorAction SilentlyContinue
$manifestArgs = @('manifest', '--file', $exe, '--version', $version, '--min', $minSupported, '--out', $manifest)
if ($Notes) { $manifestArgs += @('--notes', $Notes) }
dotnet $ashRelease @manifestArgs
if ($LASTEXITCODE) { throw "AshRelease manifest failed (exit $LASTEXITCODE)" }
if ($signKey) {
    dotnet $ashRelease sign --key $signKey --in $manifest
    if ($LASTEXITCODE) { throw "AshRelease sign failed (exit $LASTEXITCODE)" }
    if (Test-Path $pubKey) {
        dotnet $ashRelease verify --pub $pubKey --in $manifest
        if ($LASTEXITCODE) { throw "signature does not verify against $pubKey (wrong signing key?)" }
    } else { Write-Warning "update-key.pub missing in repo root: signature not verified" }
} else { Write-Warning "ASH_SIGNING_KEY not set: dist/ASH.json is NOT signed" }
$plugin = "$root\src\ASH.Plugin\bin\Release\net48"
$zip = "$dist\ASH-Plugin-$version.zip"
Compress-Archive -Path "$plugin\ASH.Plugin.dll", "$plugin\ASH.Core.dll" -DestinationPath $zip -Force
$pub = "$root\src\ASH.Runner\bin\Release\publish"
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
dotnet publish "$root\src\ASH.Runner" -c Release -r win-x64 -o $pub -nologo -v q
if ($LASTEXITCODE) { throw "dotnet publish ash-run failed (exit $LASTEXITCODE)" }
$runnerZip = "$dist\ASH-Runner-$version-win-x64.zip"
Compress-Archive -Path "$pub\ash-run.exe" -DestinationPath $runnerZip -Force
Get-Item $exe, $zip, $runnerZip, $manifest, "$manifest.sig" -ErrorAction SilentlyContinue | ForEach-Object { Write-Host ("{0}  {1:N0} bytes" -f $_.FullName, $_.Length) }
