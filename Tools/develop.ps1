[CmdletBinding()]
param(
    [ValidateSet('Check', 'Test', 'BuildAndroid')]
    [string]$Task = 'Check'
)

$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $repoPath 'WakayamaAR_Photo(proto)'
$previousLocation = Get-Location
$previousUpdateCheck = $env:UNITY_NO_UPDATE_CHECK
$env:UNITY_NO_UPDATE_CHECK = '1'
$measurements = [System.Collections.Generic.List[object]]::new()
$runId = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$runDirectory = Join-Path $repoPath "Artifacts/Runs/$runId"
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$startedAt = [DateTime]::UtcNow.ToString('o')
$script:currentStep = 'Initialization'
$failure = $null
$taskExit = 0
$apkRecord = $null

function Invoke-Checked {
    param([string]$Step, [string]$Executable, [string[]]$Arguments, [string]$OutputPath)
    $script:currentStep = $Step
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $commandExit = $null
    $lines = [System.Collections.Generic.List[string]]::new()
    try {
        if ($OutputPath) {
            & $Executable @Arguments | ForEach-Object { $lines.Add([string]$_); Write-Output $_ }
        } else { & $Executable @Arguments }
        $commandExit = $LASTEXITCODE
    }
    finally {
        $watch.Stop()
        $measurements.Add([pscustomobject]@{
            step = $Step; seconds = [Math]::Round($watch.Elapsed.TotalSeconds, 2); exitCode = $commandExit
            status = $(if ($commandExit -eq 0 -and $null -ne $commandExit) { 'passed' } else { 'failed' })
        })
        if ($OutputPath) { [IO.File]::WriteAllLines($OutputPath, $lines.ToArray(), [Text.UTF8Encoding]::new($false)) }
    }
    if ($commandExit -ne 0) { throw "$Step failed (exit $commandExit). See the run logs." }
}

function Invoke-UnityTests {
    param([string]$Mode, [string]$Filter)
    $reportPath = Join-Path $runDirectory "$Mode.xml"
    $logPath = Join-Path $runDirectory "$Mode.log"
    Invoke-Checked -Step $Mode -Executable 'unity' -Arguments @(
        'test', $projectPath, '--editor-version', '6000.2.10f1', '--mode', $Mode,
        '--filter', $Filter, '--output', $reportPath, '--timeout', '600', '--format', 'json',
        '--', '-logFile', $logPath, '-buildTarget', 'Android', '-nographics'
    )
    Invoke-Checked -Step "$Mode report" -Executable 'python' -Arguments @(
        '-B', 'Tools/run_report.py', 'validate-tests', $reportPath, '--suite', $Filter
    )
}

try {
    Set-Location -LiteralPath $repoPath
    Invoke-Checked -Step 'Static validation' -Executable 'python' -Arguments @(
        '-B', 'Tools/validate_project.py', '--json'
    ) -OutputPath (Join-Path $runDirectory 'StaticValidation.json')
    Invoke-Checked -Step 'Tool regression tests' -Executable 'python' -Arguments @(
        '-B', 'Tools/run_tool_tests.py'
    ) -OutputPath (Join-Path $runDirectory 'ToolTests.json')
    if ($Task -ne 'Check') {
        Invoke-UnityTests -Mode 'EditMode' -Filter 'PhotoGeometryTests'
        Invoke-UnityTests -Mode 'PlayMode' -Filter 'PhotoWorkflowTests'
    }
    if ($Task -eq 'BuildAndroid') {
        $apkPath = Join-Path $repoPath 'Artifacts/Builds/Android/WakayamaAR.apk'
        Invoke-Checked -Step 'Android build' -Executable 'unity' -Arguments @(
            'build', $projectPath, '--editor-version', '6000.2.10f1', '--target', 'Android',
            '--execute-method', 'PhotoBuild.BuildAndroid', '--output-path', $apkPath,
            '--log-file', (Join-Path $runDirectory 'Android.log'), '--timeout', '1800',
            '--provenance-path', (Join-Path $runDirectory 'build-provenance.json'),
            '--allow-dirty-build', '--no-tail', '--format', 'json'
        )
        $script:currentStep = 'APK verification'
        $watch = [Diagnostics.Stopwatch]::StartNew()
        try {
            if (!(Test-Path -LiteralPath $apkPath)) { throw 'Build returned without an APK.' }
            $digest = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash
            $apkRecord = @{ path = $apkPath; sha256 = $digest }
        }
        finally {
            $watch.Stop()
            $measurements.Add([pscustomobject]@{
                step = 'APK verification'; seconds = [Math]::Round($watch.Elapsed.TotalSeconds, 2)
                exitCode = $(if ($apkRecord) { 0 } else { 1 }); status = $(if ($apkRecord) { 'passed' } else { 'failed' })
            })
        }
    }
}
catch {
    $failure = @{ stage = $script:currentStep; message = $_.Exception.Message }
    $taskExit = 1
    Write-Error $_ -ErrorAction Continue
}
finally {
    try {
        $measurements | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runDirectory 'timings.json') -Encoding UTF8
        $state = @{
            schemaVersion = 1; task = $Task; startedAtUtc = $startedAt; finishedAtUtc = [DateTime]::UtcNow.ToString('o')
            outcome = $(if ($taskExit -eq 0) { 'passed' } else { 'failed' }); failure = $failure
            steps = @($measurements.ToArray()); apk = $apkRecord
        }
        $state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'run-state.json') -Encoding UTF8
        & python (Join-Path $PSScriptRoot 'run_report.py') summarize $runDirectory
        if ($LASTEXITCODE -ne 0) { $taskExit = 1 }
    }
    catch {
        $taskExit = 1
        Write-Error "Could not write the run summary: $($_.Exception.Message)" -ErrorAction Continue
    }
    finally {
        Write-Output "Run artifacts: $runDirectory"
        $env:UNITY_NO_UPDATE_CHECK = $previousUpdateCheck
        Set-Location -LiteralPath $previousLocation
    }
}
exit $taskExit
