# Uploads dist/ASH.json, dist/ASH.json.sig (if present) and the Setup exe named in the manifest to an ASH.Server.
#   publish-release.ps1 -Server http://host:5080 -Token <Admin:Token> [-Dist dist] [-Force]
# -Force also allows publishing a version lower than the current one (rollback).
# Uses System.Net.Http.HttpClient so it behaves the same on Windows PowerShell 5.1 and PowerShell 7 (streams the exe, no -Form needed).
param(
    [Parameter(Mandatory)][string]$Server,
    [Parameter(Mandatory)][string]$Token,
    [string]$Dist = 'dist',
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$Dist = (Resolve-Path $Dist).Path
$manifestPath = Join-Path $Dist 'ASH.json'
if (-not (Test-Path $manifestPath)) { throw "$manifestPath not found (run scripts\build-release.ps1)" }
$setupName = (Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json).file
$setupPath = Join-Path $Dist $setupName
if (-not (Test-Path $setupPath)) { throw "$setupPath not found (manifest names $setupName)" }
$sigPath = "$manifestPath.sig"

$streams = @()
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromMinutes(30)
try {
    $form = New-Object System.Net.Http.MultipartFormDataContent
    $parts = @(@('manifest', $manifestPath, 'ASH.json'), @('setup', $setupPath, $setupName))
    if (Test-Path $sigPath) { $parts += , @('signature', $sigPath, 'ASH.json.sig') } else { Write-Warning 'ASH.json.sig not found: uploading unsigned' }
    foreach ($p in $parts) {
        $fs = [IO.File]::OpenRead($p[1]); $streams += $fs
        $form.Add((New-Object System.Net.Http.StreamContent($fs)), $p[0], $p[2])
    }
    $url = $Server.TrimEnd('/') + '/api/admin/releases' + $(if ($Force) { '?force=1' } else { '' })
    $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, $url)
    $req.Headers.Add('X-ASH-Admin-Token', $Token)
    $req.Content = $form
    $resp = $client.SendAsync($req).GetAwaiter().GetResult()
    $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Write-Host ("{0} {1}" -f [int]$resp.StatusCode, $resp.ReasonPhrase)
    Write-Host $body
    if (-not $resp.IsSuccessStatusCode) { exit 1 }
} finally {
    foreach ($s in $streams) { $s.Dispose() }
    $client.Dispose()
}
