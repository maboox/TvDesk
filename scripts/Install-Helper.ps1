[CmdletBinding()]
param([string]$Destination = (Join-Path (Split-Path $PSScriptRoot) 'tools'))
$ErrorActionPreference = 'Stop'
$pin = Get-Content (Join-Path (Split-Path $PSScriptRoot) 'src/Resources/tools.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$temp = Join-Path $Destination 'yt-dlp.exe.download'
$exe = Join-Path $Destination 'yt-dlp.exe'
if ((Test-Path -LiteralPath $exe) -and (Get-FileHash $exe -Algorithm SHA256).Hash -eq $pin.sha256) {
    Write-Host "Verified yt-dlp $($pin.version)"
    return
}
try {
    Invoke-WebRequest -Uri $pin.url -OutFile $temp
    if ((Get-FileHash $temp -Algorithm SHA256).Hash -ne $pin.sha256) { throw 'yt-dlp SHA-256 mismatch. Installation stopped.' }
    Move-Item -LiteralPath $temp -Destination $exe -Force
    Write-Host "Verified yt-dlp $($pin.version)"
} finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp }
}
