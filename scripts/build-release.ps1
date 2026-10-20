# Release build -> dist/ASH-Setup-<Version>.exe (single exe, plugin DLLs embedded), dist/ASH-Plugin-<Version>.zip (manual NETLOAD)
# and dist/ASH-Runner-<Version>-win-x64.zip (ash-run.exe, self-contained single file: no .NET install needed)
# and dist/ASH-Server-<Version>-win-x64.zip (ASH.Server.exe self-contained single file + appsettings.json + README-server.txt).
# Version comes from <Version> in Directory.Build.props.
# Also dist/ASH.json (release manifest: version, minSupported, SHA-256/size of the Setup exe)
# and dist/ASH.json.sig, signed with the private-key PEM at $env:ASH_SIGNING_KEY (AshRelease keygen) and verified against update-key.pub.
#   -MinSupported <ver>  PCs below this version are told to update (default: $env:ASH_MIN_SUPPORTED, else 0.0.0 = nobody is "below minimum").
#                        It never defaults to the current version. Must not exceed the version being built.
#   -AllowUnsigned       DEV ONLY: build without signing (no ASH_SIGNING_KEY / no update-key.pub). Without it the build throws when either is missing.
#                        Even with it, a repo-root update-key.pub without the signing key is an error (a plugin holding the key cannot update from an unsigned manifest).
#   -RequireSignature    obsolete no-op (signing is required by default).
#   -Notes <file>        becomes the manifest notes.
# This file is UTF-8 with BOM: Windows PowerShell 5.1 would otherwise read the Korean text in the messages as ANSI.
param([switch]$AllowUnsigned, [switch]$RequireSignature, [string]$MinSupported, [string]$Notes)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath("$PSScriptRoot\..")
$version = ([xml](Get-Content "$root\Directory.Build.props" -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Directory.Build.props has no <Version>" }
if ($RequireSignature) { Write-Warning "-RequireSignature is obsolete: signing is required by default (use -AllowUnsigned for dev builds)" }
$signKey = $env:ASH_SIGNING_KEY
$pubKey = "$root\update-key.pub"
$haveKey = [bool]($signKey -and (Test-Path $signKey))
$havePub = Test-Path $pubKey
if ($havePub -and -not $haveKey) { throw "update-key.pub exists but ASH_SIGNING_KEY is unset or its file is missing - 키가 든 플러그인과 서명 없는 매니페스트 조합 불가 (even with -AllowUnsigned). Set `$env:ASH_SIGNING_KEY to the signing key PEM path." }
if (-not $AllowUnsigned -and -not $haveKey) { throw "Signing is required: set `$env:ASH_SIGNING_KEY to the signing key PEM path (AshRelease keygen). Dev builds only: -AllowUnsigned" }
if (-not $AllowUnsigned -and -not $havePub) { throw "Signing is required: $pubKey is missing (AshRelease keygen, then copy update-key.pub to the repo root). Dev builds only: -AllowUnsigned" }
$sign = $haveKey -and $havePub # -AllowUnsigned with a key but no update-key.pub: nothing could verify the signature, so it is not signed
if (-not $sign) { Write-Warning "-AllowUnsigned: dist/ASH.json will NOT be signed and the plugin/server carry no update key. Development use only." }
if ($Notes -and -not (Test-Path $Notes)) { throw "-Notes file not found: $Notes" }
$minSupported = if ($MinSupported) { $MinSupported } elseif ($env:ASH_MIN_SUPPORTED) { $env:ASH_MIN_SUPPORTED } else { '0.0.0' }
dotnet build "$root\ASH.sln" -c Release --no-incremental -nologo -v q
if ($LASTEXITCODE) { throw "dotnet build failed (exit $LASTEXITCODE)" }
$ashRelease = "$root\tools\AshRelease\bin\Release\net10.0\AshRelease.dll"
if ($havePub) { # a stale or skipped compile would ship a plugin/server without the update key
    foreach ($dll in "$root\src\ASH.Plugin\bin\Release\net48\ASH.Plugin.dll", "$root\src\ASH.Server\bin\Release\net10.0\ASH.Server.dll") {
        dotnet $ashRelease check-resource --dll $dll --name update-key.pub
        if ($LASTEXITCODE) { throw "$dll does not embed update-key.pub (stale build output?)" }
    }
}
dotnet test "$root\ASH.sln" -c Release --no-build -nologo -v q
if ($LASTEXITCODE) { throw "dotnet test failed (exit $LASTEXITCODE); release not packaged" }
$dist = "$root\dist"
New-Item -ItemType Directory -Force $dist | Out-Null
$exe = "$dist\ASH-Setup-$version.exe"
Copy-Item "$root\src\ASH.Installer\bin\Release\net48\ASH.Installer.exe" $exe -Force
$manifest = "$dist\ASH.json"
Remove-Item $manifest, "$manifest.sig" -Force -ErrorAction SilentlyContinue
$manifestArgs = @('manifest', '--file', $exe, '--version', $version, '--min', $minSupported, '--out', $manifest)
if ($Notes) { $manifestArgs += @('--notes', $Notes) }
dotnet $ashRelease @manifestArgs
if ($LASTEXITCODE) { throw "AshRelease manifest failed (exit $LASTEXITCODE)" }
if ($sign) {
    dotnet $ashRelease sign --key $signKey --in $manifest
    if ($LASTEXITCODE) { throw "AshRelease sign failed (exit $LASTEXITCODE)" }
    dotnet $ashRelease verify --pub $pubKey --in $manifest
    if ($LASTEXITCODE) { throw "signature does not verify against $pubKey (wrong signing key?)" }
}
$plugin = "$root\src\ASH.Plugin\bin\Release\net48"
$zip = "$dist\ASH-Plugin-$version.zip"
Compress-Archive -Path "$plugin\ASH.Plugin.dll", "$plugin\ASH.Core.dll" -DestinationPath $zip -Force
# dotnet publish has no --no-incremental: delete the RID-specific intermediates so the publish below compiles from scratch
$rid = "obj\Release\net10.0\win-x64"
$pub = "$root\src\ASH.Runner\bin\Release\publish"
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
Remove-Item "$root\src\ASH.Runner\$rid" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$root\src\ASH.Runner" -c Release -r win-x64 -o $pub -nologo -v q
if ($LASTEXITCODE) { throw "dotnet publish ash-run failed (exit $LASTEXITCODE)" }
$runnerZip = "$dist\ASH-Runner-$version-win-x64.zip"
Compress-Archive -Path "$pub\ash-run.exe" -DestinationPath $runnerZip -Force
$serverPub = "$root\src\ASH.Server\bin\Release\publish"
if (Test-Path $serverPub) { Remove-Item $serverPub -Recurse -Force }
Remove-Item "$root\src\ASH.Server\$rid" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$root\src\ASH.Server" -c Release -r win-x64 -o $serverPub -nologo -v q
if ($LASTEXITCODE) { throw "dotnet publish ASH.Server failed (exit $LASTEXITCODE)" }
if ($havePub) { # the single-file exe bundles this assembly; the published one is the RID build, not the sln build checked above
    dotnet $ashRelease check-resource --dll "$root\src\ASH.Server\$rid\ASH.Server.dll" --name update-key.pub
    if ($LASTEXITCODE) { throw "published ASH.Server does not embed update-key.pub" }
}
$serverZip = "$dist\ASH-Server-$version-win-x64.zip"
Compress-Archive -Path "$serverPub\ASH.Server.exe", "$serverPub\appsettings.json", "$root\src\ASH.Server\README-server.txt" -DestinationPath $serverZip -Force
Get-Item $exe, $zip, $runnerZip, $serverZip, $manifest, "$manifest.sig" -ErrorAction SilentlyContinue | ForEach-Object { Write-Host ("{0}  {1:N0} bytes" -f $_.FullName, $_.Length) }
