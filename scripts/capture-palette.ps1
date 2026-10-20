# Renders the ASH palette outside ZWCAD to docs/images/palette-preview.png (280x900). Needs only the .NET SDK.
param([int]$Tab = 0, [string]$Out = "$PSScriptRoot\..\docs\images\palette-preview.png")
$ErrorActionPreference = 'Stop'
dotnet build "$PSScriptRoot\..\src\ASH.Plugin\ASH.Plugin.csproj" -c Debug -v q -nologo
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet build "$PSScriptRoot\PaletteHost\PaletteHost.csproj" -c Debug -v q -nologo
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$out = [IO.Path]::GetFullPath($Out)
$p = Start-Process "$PSScriptRoot\PaletteHost\bin\Debug\net48\PaletteHost.exe" -ArgumentList "`"$out`"", $Tab -Wait -PassThru
if ($p.ExitCode) { exit $p.ExitCode }
Write-Host "saved $out"
