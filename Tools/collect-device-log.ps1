[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DeviceSerial,
    [string]$AdbPath = 'C:/Program Files/Unity/Hub/Editor/6000.2.10f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe'
)

$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
if (!(Test-Path -LiteralPath $AdbPath)) { throw 'adb not found. Supply -AdbPath.' }
$settings = Get-Content -LiteralPath (Join-Path $repoPath 'WakayamaAR_Photo(proto)/ProjectSettings/ProjectSettings.asset') -Raw
$packageMatch = [regex]::Match($settings, '(?m)^    Android: ([A-Za-z0-9_.]+)\r?$')
if (!$packageMatch.Success) { throw 'Android application identifier not found.' }
$package = $packageMatch.Groups[1].Value
$appProcess = & $AdbPath -s $DeviceSerial shell pidof $package
if ($LASTEXITCODE -ne 0 -or !$appProcess -or $appProcess.Trim() -notmatch '^\d+$') {
    throw "Start $package on the selected device before collecting logs."
}
$logDirectory = Join-Path $repoPath 'Artifacts/DeviceLogs'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$logPath = Join-Path $logDirectory ((Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
& $AdbPath -s $DeviceSerial logcat -d -t 2000 --pid=$($appProcess.Trim()) -v threadtime |
    Set-Content -LiteralPath $logPath -Encoding UTF8
if ($LASTEXITCODE -ne 0) { throw 'adb logcat failed.' }
Write-Output "Collected current app process logs: $logPath"
