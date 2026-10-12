param(
    [string]$ProjectPath = (Split-Path -Parent $PSScriptRoot),
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.0.38f1\Editor\Unity.exe',
    [string]$TemporaryRoot = $env:TEMP
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath $ProjectPath).Path
$buildRoot = Join-Path $TemporaryRoot ('CodexHandPose-' + [Guid]::NewGuid().ToString('N'))
$buildRoot = [System.IO.Path]::GetFullPath($buildRoot)
if ($buildRoot -match '[^\x00-\x7F]') {
    throw 'Android requires an ASCII project path. Supply -TemporaryRoot with an ASCII directory.'
}
if (-not (Test-Path -LiteralPath $UnityEditor)) { throw "Unity Editor not found: $UnityEditor" }
New-Item -ItemType Directory -Path $buildRoot | Out-Null
foreach ($name in @('Assets', 'Packages', 'ProjectSettings')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $buildRoot -Recurse
}
$logsRoot = Join-Path $projectRoot 'Validation\Logs'
$outputRoot = Join-Path $projectRoot 'Builds'
New-Item -ItemType Directory -Force -Path $logsRoot, $outputRoot | Out-Null
$logPath = Join-Path $logsRoot 'android-build-ascii.log'
Set-Content -LiteralPath (Join-Path $logsRoot 'android-build-project-path.txt') -Value $buildRoot -Encoding utf8
$arguments = @('-batchmode', '-nographics', '-quit', '-buildTarget', 'Android', '-projectPath', ('"' + $buildRoot + '"'),
    '-executeMethod', 'HandPose.Editor.HandPoseProjectSetup.BuildAndroid', '-logFile', ('"' + $logPath + '"'))
Write-Output "Building in ASCII copy: $buildRoot"
$process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
$reportPath = Join-Path $buildRoot 'Builds\build-result.txt'
if (Test-Path -LiteralPath $reportPath) { Copy-Item -LiteralPath $reportPath -Destination $outputRoot -Force }
if ($process.ExitCode -ne 0) { throw "Unity build failed (exit $($process.ExitCode)); inspect $logPath" }
$apkPath = Join-Path $buildRoot 'Builds\HandPose-PICO4Ultra.apk'
if (-not (Test-Path -LiteralPath $apkPath)) { throw "APK was not generated; inspect $logPath" }
Copy-Item -LiteralPath $apkPath -Destination $outputRoot -Force
Write-Output "APK copied to $outputRoot"
Write-Output "Temporary project retained for inspection: $buildRoot"
