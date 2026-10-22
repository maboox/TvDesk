[CmdletBinding()]
param([string]$Output = (Join-Path (Split-Path $PSScriptRoot) 'publish/TvDesk'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
& (Join-Path $PSScriptRoot 'Install-Helper.ps1')
dotnet publish (Join-Path $root 'src/TvDesk.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:RestoreLockedMode=true -o $Output
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
foreach ($item in @('LICENSE','THIRD-PARTY-NOTICES.md','PRIVACY.md','README.md','docs','licenses')) {
    Copy-Item -LiteralPath (Join-Path $root $item) -Destination $Output -Recurse -Force
}
$cache = (dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', ''
$deps = Get-Content (Join-Path $Output 'TvDesk.deps.json') -Raw | ConvertFrom-Json
$runtimeLibraries = $deps.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'runtimepack.*' }
foreach ($library in $runtimeLibraries) {
    $parts = $library.Substring('runtimepack.'.Length).Split('/')
    $folder = Join-Path $cache (Join-Path $parts[0].ToLowerInvariant() $parts[1])
    foreach ($notice in @('LICENSE','LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
        $file = Join-Path $folder $notice
        if (Test-Path -LiteralPath $file) {
            Copy-Item -LiteralPath $file -Destination (Join-Path $Output "licenses/$($parts[0])-$notice") -Force
        }
    }
}
$runtimeLibraries | Set-Content (Join-Path $Output 'licenses/runtime-versions.txt') -Encoding utf8
foreach ($item in @('TvDesk.exe','yt-dlp.exe','libvlc','licenses/LGPL-2.1.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $Output $item))) { throw "Missing release file: $item" }
}
$pin = Get-Content (Join-Path $root 'src/Resources/tools.json') -Raw | ConvertFrom-Json
if ((Get-FileHash (Join-Path $Output 'yt-dlp.exe')).Hash -ne $pin.sha256) { throw 'Bundled helper integrity check failed' }
Write-Host "Portable application ready: $Output"
