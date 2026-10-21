# Release build -> dist/ASH-Setup-<Version>.exe (single exe, plugin DLLs embedded), dist/ASH-Plugin-<Version>.zip (manual NETLOAD)
# and dist/ASH-Runner-<Version>-win-x64.zip (ash-run.exe, self-contained single file: no .NET install needed)
# and dist/ASH-Server-<Version>-win-x64.zip (ASH.Server.exe self-contained single file + appsettings.json + README-server.txt + install-service.bat/uninstall-service.bat).
# Version comes from <Version> in Directory.Build.props.
# Also dist/ASH.json (release manifest: version, minSupported, SHA-256/size of the Setup exe)
# and dist/ASH.json.sig, signed with the private-key PEM at $env:ASH_SIGNING_KEY (AshRelease keygen) and verified against update-key.pub.
# and dist/ASH.Server.json(.sig): version/SHA-256/size of the server zip, signed with the same key (running servers replace themselves from it).
# and dist/ASH.Runner.json(.sig): the same for the runner zip (ash-run replaces itself from it, through the server).
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
# ZWCAD has no binding redirects for the plugin's private dependencies. The plugin's assembly resolver (BundledAssemblies) answers a request with the dll in the
# plugin folder, but only if that one is the same version or newer; a dll asking for a NEWER version than the folder holds cannot work (found with System.Memory).
$pluginOut = "$root\src\ASH.Plugin\bin\Release\net48"
$have = @{}
foreach ($f in Get-ChildItem "$pluginOut\*.dll") { $n = [Reflection.AssemblyName]::GetAssemblyName($f.FullName); $have[$n.Name] = $n.Version }
foreach ($f in Get-ChildItem "$pluginOut\*.dll") {
    foreach ($r in [Reflection.Assembly]::ReflectionOnlyLoadFrom($f.FullName).GetReferencedAssemblies()) {
        if ($have.ContainsKey($r.Name) -and $have[$r.Name] -lt $r.Version) {
            throw "$($f.Name) needs $($r.Name) $($r.Version) but the plugin folder has $($have[$r.Name]). Reference the newer package directly (see System.Memory in ASH.Core.csproj)."
        }
    }
}
$exe = "$dist\ASH-Setup-$version.exe"
# the Setup must embed every dll the plugin output holds (a new dependency would otherwise ship missing and break the commands that use it)
foreach ($f in Get-ChildItem "$root\src\ASH.Plugin\bin\Release\net48\*.dll") {
    dotnet $ashRelease check-resource --dll "$root\src\ASH.Installer\bin\Release\net48\ASH.Installer.exe" --name $f.Name
    if ($LASTEXITCODE) { throw "ASH.Installer.exe does not embed $($f.Name): add it to InstallPlanner.Files and ASH.Installer.csproj" }
}
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
# every dll of the plugin output (ASH.Plugin, ASH.Core, ClosedXML and its dependencies): the plugin needs them next to it
Compress-Archive -Path (Get-ChildItem "$plugin\*.dll").FullName -DestinationPath $zip -Force
# dotnet publish has no --no-incremental: delete the RID-specific intermediates so the publish below compiles from scratch
$rid = "obj\Release\net10.0\win-x64"
$pub = "$root\src\ASH.Runner\bin\Release\publish"
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
Remove-Item "$root\src\ASH.Runner\$rid" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$root\src\ASH.Runner" -c Release -r win-x64 -o $pub -nologo -v q
if ($LASTEXITCODE) { throw "dotnet publish ash-run failed (exit $LASTEXITCODE)" }
if ($havePub) { # ash-run only updates itself when the signing key is inside it
    dotnet $ashRelease check-resource --dll "$root\src\ASH.Runner\$rid\ash-run.dll" --name update-key.pub
    if ($LASTEXITCODE) { throw "published ash-run does not embed update-key.pub" }
}
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
Compress-Archive -Path "$serverPub\ASH.Server.exe", "$serverPub\appsettings.json", "$root\src\ASH.Server\README-server.txt", "$root\src\ASH.Server\install-service.bat", "$root\src\ASH.Server\uninstall-service.bat" -DestinationPath $serverZip -Force
# ASH.Server.json / ASH.Runner.json (+ .sig): version, SHA-256 and size of a zip, signed with the same key.
# A running server replaces itself from the first; the server hands the second to ash-run, which replaces itself from it.
function New-PackageManifest($product, $zipPath, $name) {
    $out = "$dist\$name"
    Remove-Item $out, "$out.sig" -Force -ErrorAction SilentlyContinue
    $json = [ordered]@{ product = $product; version = $version; file = (Split-Path $zipPath -Leaf)
        sha256 = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant(); size = (Get-Item $zipPath).Length } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText($out, $json, (New-Object Text.UTF8Encoding $false))
    if ($sign) {
        dotnet $ashRelease sign --key $signKey --in $out | Out-Host
        if ($LASTEXITCODE) { throw "AshRelease sign $name failed (exit $LASTEXITCODE)" }
        dotnet $ashRelease verify --pub $pubKey --in $out | Out-Host
        if ($LASTEXITCODE) { throw "$name signature does not verify against $pubKey" }
    }
    return $out
}
$serverManifest = New-PackageManifest 'ASH.Server' $serverZip 'ASH.Server.json'
$runnerManifest = New-PackageManifest 'ASH.Runner' $runnerZip 'ASH.Runner.json'
Get-Item $exe, $zip, $runnerZip, $serverZip, $manifest, "$manifest.sig", $serverManifest, "$serverManifest.sig", $runnerManifest, "$runnerManifest.sig" -ErrorAction SilentlyContinue | ForEach-Object { Write-Host ("{0}  {1:N0} bytes" -f $_.FullName, $_.Length) }
